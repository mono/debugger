using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Remoting;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public delegate void ProcessExitedHandler (ProcessHandle handle);

	[Serializable]
	public class FrameHandle
	{
		ProcessHandle process;
		StackFrame frame;

		public FrameHandle (ProcessHandle process, StackFrame frame)
		{
			this.process = process;
			this.frame = frame;
		}

		public ProcessHandle Process {
			get { return process; }
		}

		public StackFrame Frame {
			get { return frame; }
		}

		public bool PrintSource (ScriptingContext context)
		{
			SourceAddress location = frame.SourceAddress;
			if (location == null)
				return false;

			IMethod method = frame.Method;
			if ((method == null) || !method.HasSource || (method.Source == null))
				return false;

			MethodSource source = method.Source;
			if (source.SourceBuffer == null)
				return false;

			string line = source.SourceBuffer.Contents [location.Row - 1];
			context.Print (String.Format ("{0,4} {1}", location.Row, line));
			return true;
		}

		public void Disassemble (ScriptingContext context)
		{
			AssemblerLine line = Disassemble ();

			if (line != null)
				context.PrintInstruction (line);
			else
				context.Error ("Cannot disassemble instruction at address {0}.",
					       frame.TargetAddress);
		}

		public AssemblerLine Disassemble ()
		{
			return Disassemble (frame.TargetAddress);
		}

		public AssemblerLine Disassemble (TargetAddress address)
		{
			IMethod method = frame.Method;
			if ((method == null) || !method.IsLoaded)
				return process.Process.DisassembleInstruction (null, address);
			else if ((address < method.StartAddress) || (address >= method.EndAddress))
				return process.Process.DisassembleInstruction (null, address);
			else
				return process.Process.DisassembleInstruction (method, address);
		}

		public void DisassembleMethod (ScriptingContext context)
		{
			IMethod method = frame.Method;

			if ((method == null) || !method.IsLoaded)
				throw new ScriptingException ("Selected stack frame has no method.");

			AssemblerMethod asm = process.Process.DisassembleMethod (method);
			foreach (AssemblerLine line in asm.Lines)
				context.PrintInstruction (line);
		}

		public int[] RegisterIndices {
			get { return process.Architecture.RegisterIndices; }
		}

		public string[] RegisterNames {
			get { return process.Architecture.RegisterNames; }
		}

		public Registers GetRegisters ()
		{
			Registers registers = frame.Registers;
			if (registers == null)
				throw new ScriptingException (
					"Cannot get registers of selected stack frame.");

			return registers;
		}

		public int FindRegister (string name)
		{
			return process.GetRegisterIndex (name);
		}

		public ITargetType GetRegisterType (int index)
		{
			return frame.Language.PointerType;
		}

		public TargetLocation GetRegisterLocation (int index, long offset, bool dereference)
		{
			return frame.GetRegisterLocation (index, offset, dereference, 0);
		}

		public ITargetObject GetRegister (int index, long offset)
		{
			ITargetType type = GetRegisterType (index);
			ITargetTypeInfo tinfo = type.GetTypeInfo ();
			TargetLocation location = GetRegisterLocation (index, offset, false);
			return tinfo.GetObject (location);
		}

		public void SetRegister (int index, long value)
		{
			frame.SetRegister (index, value);
		}

		public void ShowParameters (ScriptingContext context)
		{
			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] param_vars = frame.Method.Parameters;
			foreach (IVariable var in param_vars)
				context.Interpreter.Style.PrintVariable (var, this);
		}

		public void ShowLocals (ScriptingContext context)
		{
			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] local_vars = frame.Locals;
			foreach (IVariable var in local_vars)
				context.Interpreter.Style.PrintVariable (var, this);
		}

		public IVariable GetVariableInfo (string identifier, bool report_errors)
		{
			if (frame.Method == null) {
				if (!report_errors)
					return null;
				throw new ScriptingException (
					"Selected stack frame has no method.");
			}

			IVariable[] local_vars = frame.Locals;
			foreach (IVariable var in local_vars) {
				if (var.Name == identifier)
					return var;
			}

			IVariable[] param_vars = frame.Method.Parameters;
			foreach (IVariable var in param_vars) {
				if (var.Name == identifier)
					return var;
			}

			if (!report_errors)
				return null;

			throw new ScriptingException (
				"No variable or parameter with that name: `{0}'.", identifier);
		}

		public ITargetObject GetVariable (IVariable var)
		{
			if (!var.IsAlive (frame.TargetAddress))
				throw new ScriptingException ("Variable out of scope.");
			if (!var.CheckValid (frame))
				throw new ScriptingException ("Variable cannot be accessed.");

			return var.GetObject (frame);
		}

		public TargetLocation GetVariableLocation (IVariable var)
		{
			return var.GetLocation (frame);
		}

		public ILanguage Language {
			get {
				if (frame.Language == null)
					throw new ScriptingException (
						"Stack frame has no source language.");

				return frame.Language;
			}
		}

		public override string ToString ()
		{
			return frame.ToString ();
		}
	}

	[Serializable]
	public class BacktraceHandle
	{
		FrameHandle[] frames;

		public BacktraceHandle (ProcessHandle process, Backtrace backtrace)
		{
			StackFrame[] bt_frames = backtrace.Frames;
			if (bt_frames != null) {
				frames = new FrameHandle [bt_frames.Length];
				for (int i = 0; i < frames.Length; i++)
					frames [i] = new FrameHandle (process, bt_frames [i]);
			} else
				frames = new FrameHandle [0];
		}

		public int Length {
			get { return frames.Length; }
		}

		public FrameHandle this [int number] {
			get { return frames [number]; }
		}
	}

	public class ProcessHandle : MarshalByRefObject
	{
		DebuggerClient client;
		Interpreter interpreter;
		ThreadGroup tgroup;
		Process process;
		string name;
		int id;

		Hashtable registers;
		ProcessEventSink sink;

		public ProcessHandle (Interpreter interpreter, DebuggerClient client,
				      Process process)
		{
			this.interpreter = interpreter;
			this.client = client;
			this.process = process;
			this.name = process.Name;
			this.id = process.ID;

			initialize ();
		}

		public ProcessHandle (Interpreter interpreter, DebuggerClient client,
				      Process process, int pid)
			: this (interpreter, client, process)
		{
			if (process.HasTarget) {
				if (!process.IsDaemon) {
					StackFrame frame = process.CurrentFrame;
					current_frame = new FrameHandle (this, frame);
					interpreter.Print ("{0} stopped at {1}.", Name, frame);
					interpreter.Style.PrintFrame (
						interpreter.GlobalContext, current_frame);
				}
			}
		}

		public Process Process {
			get { return process; }
		}

		public ThreadGroup ThreadGroup {
			get { return tgroup; }
		}

		public DebuggerClient DebuggerClient {
			get { return client; }
		}

		public event ProcessExitedHandler ProcessExitedEvent;

		void initialize ()
		{
			registers = new Hashtable ();
			IArchitecture arch = process.Architecture;

			for (int i = 0; i < arch.RegisterNames.Length; i++) {
				string register = arch.RegisterNames [i];

				registers.Add (register, arch.AllRegisterIndices [i]);
			}

			tgroup = ThreadGroup.CreateThreadGroup ("@" + ID);
			tgroup.AddThread (ID);

			sink = new ProcessEventSink (this);
		}

		int current_frame_idx = -1;
		FrameHandle current_frame = null;
		BacktraceHandle current_backtrace = null;
		AssemblerLine current_insn = null;
		ITargetObject current_exception = null;

		protected void ProcessExited ()
		{
			process = null;

			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
		}

		protected void TargetEvent (TargetEventArgs args, FrameHandle new_frame, AssemblerLine new_insn)
		{
			current_exception = null;

			current_frame = new_frame;
			current_insn = new_insn;

			current_frame_idx = -1;
			current_backtrace = null;

			string frame = "";
			if (current_frame != null)
				frame = String.Format (" at {0}", current_frame);

			switch (args.Type) {
			case TargetEventType.TargetStopped:
				if ((int) args.Data != 0)
					interpreter.Print ("{0} received signal {1}{2}.", Name, (int) args.Data, frame);
				else if (!interpreter.IsInteractive)
					break;
				else
					interpreter.Print ("{0} stopped{1}.", Name, frame);

				if (interpreter.IsScript)
					break;

				interpreter.Style.TargetStopped (
					interpreter.GlobalContext, current_frame, current_insn);

				if (!interpreter.IsInteractive)
					interpreter.Abort ();
				break;

			case TargetEventType.Exception:
			case TargetEventType.UnhandledException:
				interpreter.Print ("{0} caught {2}exception{1}.", Name, frame,
						   args.Type == TargetEventType.Exception ? "" : "unhandled ");

				TargetAddress exc = (TargetAddress) args.Data;
				ITargetObject exc_object = null;
				if (current_frame != null)
					exc_object = current_frame.Language.CreateObject (
						current_frame.Frame.TargetAccess, exc);

				current_exception = exc_object;

				if (interpreter.IsScript)
					break;

				interpreter.Style.UnhandledException (
					interpreter.GlobalContext, current_frame, current_insn,
					exc_object);

				if (!interpreter.IsInteractive)
					interpreter.Abort ();
				break;

			case TargetEventType.TargetExited:
				if (!process.IsDaemon) {
					if ((int) args.Data == 0)
						interpreter.Print ("{0} terminated normally.", Name);
					else
						interpreter.Print ("{0} exited with exit code {1}.",
								   id, (int) args.Data);
				}
				ProcessExited ();
				break;

			case TargetEventType.TargetSignaled:
				if (!process.IsDaemon) {
					interpreter.Print ("{0} died with fatal signal {1}.",
							   id, (int) args.Data);
				}
				ProcessExited ();
				break;
			}
		}

		public void Step (WhichStepCommand which)
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			else if (!process.CanRun)
				throw new ScriptingException ("{0} cannot be executed.", Name);

			current_exception = null;

			switch (which) {
			case WhichStepCommand.Continue:
				process.Continue ();
				break;
			case WhichStepCommand.Step:
				interpreter.Style.IsNative = false;
				process.StepLine ();
				break;
			case WhichStepCommand.Next:
				interpreter.Style.IsNative = false;
				process.NextLine ();
				break;
			case WhichStepCommand.StepInstruction:
				interpreter.Style.IsNative = true;
				process.StepInstruction ();
				break;
			case WhichStepCommand.StepNativeInstruction:
				interpreter.Style.IsNative = true;
				process.StepNativeInstruction ();
				break;
			case WhichStepCommand.NextInstruction:
				interpreter.Style.IsNative = true;
				process.NextInstruction ();
				break;
			case WhichStepCommand.Finish:
				process.Finish ();
				break;
			case WhichStepCommand.FinishNative:
				process.FinishNative ();
				break;
			default:
				throw new Exception ();
			}

			if (interpreter.IsSynchronous)
				interpreter.DebuggerManager.Wait (process);
		}

		public void Stop ()
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			process.Stop ();
			if (interpreter.IsSynchronous)
				interpreter.DebuggerManager.Wait (process);
		}

		public void Background ()
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			else if (!process.CanRun)
				throw new ScriptingException ("{0} cannot be executed.", Name);
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			process.Continue (true);
		}

		public IArchitecture Architecture {
			get {
				if (process.Architecture == null)
					throw new ScriptingException ("Unknown architecture");

				return process.Architecture;
			}
		}

		public int ID {
			get {
				return id;
			}
		}

		public bool IsAlive {
			get {
				return process != null;
			}
		}

		public int CurrentFrameIndex {
			get {
				if (current_frame_idx == -1)
					return 0;

				return current_frame_idx;
			}

			set {
				GetBacktrace (-1);
				if ((value < 0) || (value >= current_backtrace.Length))
					throw new ScriptingException ("No such frame.");

				current_frame_idx = value;
				current_frame = current_backtrace [current_frame_idx];
			}
		}

		public BacktraceHandle GetBacktrace (int max_frames)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			if ((max_frames == -1) && (current_backtrace != null))
				return current_backtrace;

			current_backtrace = new BacktraceHandle (this, process.GetBacktrace (max_frames));

			if (current_backtrace == null)
				throw new ScriptingException ("No stack.");

			return current_backtrace;
		}

		public FrameHandle CurrentFrame {
			get {
				return GetFrame (current_frame_idx);
			}
		}

		public FrameHandle GetFrame (int number)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			if (number == -1) {
				if (current_frame == null)
					current_frame = new FrameHandle (this, process.CurrentFrame);

				return current_frame;
			}

			GetBacktrace (-1);
			if (number >= current_backtrace.Length)
				throw new ScriptingException ("No such frame: {0}", number);

			return current_backtrace [number];
		}

		public TargetState State {
			get {
				if (process == null)
					return TargetState.NO_TARGET;
				else
					return process.State;
			}
		}

		public ITargetObject CurrentException {
			get {
				return current_exception;
			}
		}

		public int GetRegisterIndex (string name)
		{
			if (!registers.Contains (name))
				throw new ScriptingException ("No such register: %{0}", name);

			return (int) registers [name];
		}
		
		public void Kill ()
		{
			process.Kill ();
			process = null;
			ProcessExited ();
		}

		public string Name {
			get {
				return name;
			}
		}

		public override string ToString ()
		{
			if (process == null)
				return String.Format ("Zombie @{0}", id);
			else if (process.IsDaemon)
				return String.Format ("Daemon process @{0}: {1} {2}", id, process.PID, State);
			else
				return String.Format ("Process @{0}: {1} {2}", id, process.PID, State);
		}

		[Serializable]
		private class ProcessEventSink
		{
			ProcessHandle process;

			public ProcessEventSink (ProcessHandle process)
			{
				this.process = process;

				process.process.TargetEvent += new TargetEventHandler (target_event);
				process.process.DebuggerOutput += new DebuggerOutputHandler (debugger_output);
				process.process.DebuggerError += new DebuggerErrorHandler (debugger_error);
			}

			public void target_event (TargetEventArgs args)
			{
				if (args.Frame != null) {
					FrameHandle frame = new FrameHandle (process, args.Frame);
					AssemblerLine insn = frame.Disassemble ();

					process.TargetEvent (args, frame, insn);
				} else
					process.TargetEvent (args, null, null);
			}

			public void debugger_output (string line)
			{
				process.interpreter.Print ("DEBUGGER OUTPUT: {0}", line);
			}

			public void debugger_error (object sender, string message, Exception e)
			{
				process.interpreter.Print ("DEBUGGER ERROR: {0}\n{1}", message, e);
			}

			public void process_exited ()
			{
				process.ProcessExited ();
			}
		}
	}

	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public class MultipleLocationsMatchException : ScriptingException
	{
		public readonly SourceMethod[] Sources;
		public MultipleLocationsMatchException (SourceMethod[] sources)
		  : base ("")
		{
			Sources = sources;
		}
	}

	public enum ModuleOperation
	{
		Ignore,
		UnIgnore,
		Step,
		DontStep
	}

	public enum WhichStepCommand
	{
		Continue,
		Step,
		Next,
		StepInstruction,
		StepNativeInstruction,
		NextInstruction,
		Finish,
		FinishNative
	}

	public class ScriptingContext : MarshalByRefObject
	{
		ProcessHandle current_process;
		int current_frame_idx = -1;
		Interpreter interpreter;

		ScriptingContext parent;
		ArrayList method_search_results;
		Hashtable method_search_hash;

		static AddressDomain address_domain = new AddressDomain ("scripting");

		bool is_interactive;
		bool is_synchronous;

		internal static readonly string DirectorySeparatorStr;

		static ScriptingContext ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		internal ScriptingContext (Interpreter interpreter, bool is_interactive, bool is_synchronous)
		{
			this.interpreter = interpreter;
			this.is_interactive = is_interactive;
			this.is_synchronous = is_synchronous;

			method_search_results = new ArrayList ();
			method_search_hash = new Hashtable ();
		}

		protected ScriptingContext (ScriptingContext parent)
			: this (parent.Interpreter, parent.IsInteractive, parent.IsSynchronous)
		{
			this.parent = parent;

			current_process = parent.CurrentProcess;
		}

		public ScriptingContext GetExpressionContext ()
		{
			return new ScriptingContext (this);
		}

		public ScriptingContext Parent {
			get { return parent; }
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		public DebuggerBackend GetDebuggerBackend ()
		{
			if (current_process == null)
				throw new ScriptingException ("No program to debug.");

			DebuggerBackend backend = current_process.Process.DebuggerBackend;
			if (backend == null)
				throw new ScriptingException ("No program to debug.");

			return backend;
		}

		public bool HasBackend {
			get {
				return interpreter.DebuggerManager.HasTarget;
			}
		}

		public bool IsInteractive {
			get { return is_interactive; }
		}

		public bool IsSynchronous {
			get { return is_synchronous; }
		}

		public ProcessHandle CurrentProcess {
			get { return current_process; }
			set { current_process = value; }
		}

		public FrameHandle CurrentFrame {
			get {
				return current_process.GetFrame (current_frame_idx);
			}
		}

		public int CurrentFrameIndex {
			get {
				return current_frame_idx;
			}

			set {
				current_frame_idx = value;
			}
		}

		public string[] GetNamespaces (FrameHandle frame)
		{
			IMethod method = frame.Frame.Method;
			if ((method == null) || !method.HasSource)
				return null;

			MethodSource msource = method.Source;
			if (msource.IsDynamic)
				return null;

			return msource.GetNamespaces ();
		}

		public string[] GetNamespaces ()
		{
			return GetNamespaces (CurrentFrame);
		}

		public SourceLocation CurrentLocation {
			get {
				StackFrame frame = CurrentFrame.Frame;
				if ((frame.SourceAddress == null) ||
				    (frame.SourceAddress.Location == null))
					throw new ScriptingException (
						"Current location doesn't have source code");

				return frame.SourceAddress.Location;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}

		public void Error (string message)
		{
			interpreter.Error (message);
		}

		public void Error (string format, params object[] args)
		{
			interpreter.Error (String.Format (format, args));
		}

		public void Error (ScriptingException ex)
		{
			interpreter.Error (ex);
		}

		public void Print (string message)
		{
			interpreter.Print (message);
		}

		public void Print (string format, params object[] args)
		{
			interpreter.Print (String.Format (format, args));
		}

		public void Print (object obj)
		{
			interpreter.Print (obj);
		}

		public void PrintObject (object obj)
		{
			string formatted;
			try {
				formatted = interpreter.Style.FormatObject (CurrentFrame, obj);
			} catch {
				formatted = "<cannot display object>";
			}
			Print (formatted);
		}

		public void PrintType (ITargetType type)
		{
			string formatted;
			try {
				formatted = interpreter.Style.FormatType (type);
			} catch {
				formatted = "<cannot display type>";
			}
			Print (formatted);
		}

		public void PrintInstruction (AssemblerLine line)
		{
			if (line.Label != null)
				Print ("{0}:", line.Label);
			Print ("{0:11x}\t{1}", line.Address, line.Text);
		}

		public void AddMethodSearchResult (SourceMethod[] methods, bool print)
		{
			ClearMethodSearchResults ();

			if (print)
				interpreter.Print ("More than one method matches your query:");

			foreach (SourceMethod method in methods) {
				int id = AddMethodSearchResult (method);
				if (print)
					interpreter.Print ("{0,4}  {1}", id, method.Name);
			}

			if (print)
				interpreter.Print ("\nYou may use either the full method signature,\n" +
						   "'-id N' where N is the number to the left of the method, or\n" +
						   "'-all' to select all methods.");
		}

		public void PrintMethods (SourceMethod[] methods)
		{
			foreach (SourceMethod method in methods) {
				int id = AddMethodSearchResult (method);
				interpreter.Print ("{0,4}  {1}", id, method.Name);
			}
		}

		public void PrintMethods (SourceFile source)
		{
			Print ("Methods from source file {0}: {1}", source.ID, source.FileName);
			PrintMethods (source.Methods);
		}

		public int AddMethodSearchResult (SourceMethod method)
		{
			if (method_search_hash.Contains (method.Name))
				return (int) method_search_hash [method.Name];

			int index = method_search_results.Count + 1;
			method_search_hash.Add (method.Name, index);
			method_search_results.Add (method);
			return index;
		}

		public SourceMethod GetMethodSearchResult (int index)
		{
			if ((index < 1) || (index > method_search_results.Count))
				throw new ScriptingException (
					"No such item in the method history.");

			return (SourceMethod) method_search_results [index - 1];
		}

		public int NumMethodSearchResults
		{
			get {
				return method_search_results.Count;
			}
		}

		public void ClearMethodSearchResults ()
		{
			method_search_hash = new Hashtable ();
			method_search_results = new ArrayList ();
		}

		int last_line = -1;
		string[] current_source_code = null;

		public void ListSourceCode (SourceLocation location, int count)
		{
			int start;

			if ((location == null) && (current_source_code == null))
				location = CurrentLocation;
			if (location == null) {
				if (count < 0){
					start = System.Math.Max (last_line + 2 * count, 0);
					count = -count;
				} else 
					start = last_line;
			} else {
				ISourceBuffer buffer;

				if (location.HasSourceFile) {
					string filename = location.SourceFile.FileName;
					buffer = FindFile (filename);
					if (buffer == null)
						throw new ScriptingException (
							"Cannot find source file `{0}'", filename);
				} else
					buffer = location.SourceBuffer;

				current_source_code = buffer.Contents;

				if (count < 0)
					start = System.Math.Max (location.Line + 2, 0);
				else 
					start = System.Math.Max (location.Line - 2, 0);
			}

			last_line = System.Math.Min (start + count, current_source_code.Length);

			if (start > last_line){
				int t = start;
				start = last_line;
				last_line = t;
			}

			for (int line = start; line < last_line; line++)
				interpreter.Print (String.Format ("{0,4} {1}", line + 1, current_source_code [line]));
		}

		public void ResetCurrentSourceCode ()
		{
			current_source_code = null;
			last_line = -1;
		}

		public void Dump (object obj)
		{
			if (obj == null)
				Print ("null");
			else if (obj is ITargetObject)
				Print (DumpObject ((ITargetObject) obj));
			else
				Print ("unknown:{0}:{1}", obj.GetType (), obj);
		}

		public string DumpObject (ITargetObject obj)
		{
			long dynamic = obj.TypeInfo.HasFixedSize ? -1 : obj.DynamicSize;
			return String.Format ("object:{0}:{1}:{2}", obj.IsValid,
					      dynamic, DumpType (obj.TypeInfo));
		}

		public string DumpType (ITargetTypeInfo type)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (type.Type.Name);
			sb.Append (":");
			sb.Append (type.HasFixedSize);
			sb.Append (":");
			sb.Append (type.Size);
			sb.Append (":");
			sb.Append (type.Type.Kind);
			sb.Append (" ");

			switch (type.Type.Kind) {
			case TargetObjectKind.Fundamental:
				sb.Append (((ITargetFundamentalType) type.Type).Type);
				break;

			case TargetObjectKind.Pointer: {
				ITargetPointerType ptype = (ITargetPointerType) type.Type;
				sb.Append (ptype.IsTypesafe);
				sb.Append (":");
				sb.Append (ptype.HasStaticType);
				if (ptype.HasStaticType) {
					sb.Append (":");
					sb.Append (ptype.StaticType.Name);
				}
				break;
			}

			case TargetObjectKind.Array:
				sb.Append (((ITargetArrayType) type.Type).ElementType.Name);
				break;

			case TargetObjectKind.Alias: {
				ITargetTypeAlias alias = (ITargetTypeAlias) type.Type;
				sb.Append (alias.TargetName);
				if (alias.TargetType != null) {
					sb.Append (":");
					sb.Append (alias.TargetType.Name);
				}
				break;
			}

			}

			return sb.ToString ();
		}

		public string GetFullPathByFilename (string filename)
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			try {
				backend.ModuleManager.Lock ();

				Module[] modules = backend.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.SymbolFile.Sources) {
						if (filename.Equals (source.Name))
							return source.FileName;
					}
				}
			} finally {
				backend.ModuleManager.UnLock ();
			}

			return null;
		}


		public string GetFullPath (string filename)
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			if (Path.IsPathRooted (filename))
				return filename;

			string path = GetFullPathByFilename (filename);
			if (path == null)
				path = String.Concat (
					backend.ProcessStart.BaseDirectory, DirectorySeparatorStr,
					filename);

			return path;
		}

		public SourceLocation FindLocation (string file, int line)
		{
			string path = GetFullPath (file);
			DebuggerBackend backend = GetDebuggerBackend ();
			SourceLocation location = backend.FindLocation (path, line);

			if (location != null)
				return location;
			else
				throw new ScriptingException ("No method contains the specified file/line.");
		}

		public SourceLocation FindLocation (SourceLocation location, int line)
		{
			if (location.HasSourceFile)
				return FindLocation (location.SourceFile.FileName, line);

			if (line > location.SourceBuffer.Contents.Length)
				throw new ScriptingException ("Requested line is outside the current buffer.");

			return new SourceLocation (location.Module, location.SourceBuffer, line);
		}

		public SourceLocation FindMethod (string name)
		{
			DebuggerBackend backend = GetDebuggerBackend ();
			return backend.FindMethod (name);
		}

		public Module[] GetModules (int[] indices)
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			try {
				backend.ModuleManager.Lock ();

				int pos = 0;
				Module[] retval = new Module [indices.Length];

				Module[] modules = backend.Modules;

				foreach (int index in indices) {
					if ((index < 0) || (index > modules.Length))
						throw new ScriptingException ("No such module {0}.", index);

					retval [pos++] = modules [index];
				}

				return retval;
			} finally {
				backend.ModuleManager.UnLock ();
			}
		}

		public Module[] Modules 	{
			get {
				DebuggerBackend backend = GetDebuggerBackend ();
				return backend.Modules;
			}
		}

		public SourceFile[] GetSources (int[] indices)
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			try {
				backend.ModuleManager.Lock ();

				Hashtable source_hash = new Hashtable ();

				Module[] modules = backend.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.SymbolFile.Sources)
						source_hash.Add (source.ID, source);
				}

				int pos = 0;
				SourceFile[] retval = new SourceFile [indices.Length];

				foreach (int index in indices) {
					SourceFile source = (SourceFile) source_hash [index];
					if (source == null)
						throw new ScriptingException (
							"No such source file: {0}", index);

					retval [pos++] = source;
				}

				return retval;
			} finally {
				backend.ModuleManager.UnLock ();
			}
		}

		public void ShowModules ()
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			try {
				backend.ModuleManager.Lock ();
				Module[] modules = backend.Modules;

				Print ("{0,4} {1,5} {2,5} {3}", "Id", "step?", "sym?", "Name");
				for (int i = 0; i < modules.Length; i++) {
					Module module = modules [i];

					Print ("{0,4} {1,5} {2,5} {3}",
					       i,
					       module.StepInto ? "y " : "n ",
					       module.SymbolsLoaded ? "y " : "n ",
					       module.Name);
				}
			} finally {
				backend.ModuleManager.UnLock ();
			}
		}

		void module_operation (Module module, ModuleOperation[] operations)
		{
			foreach (ModuleOperation operation in operations) {
				switch (operation) {
				case ModuleOperation.Ignore:
					module.LoadSymbols = false;
					break;
				case ModuleOperation.UnIgnore:
					module.LoadSymbols = true;
					break;
				case ModuleOperation.Step:
					module.StepInto = true;
					break;
				case ModuleOperation.DontStep:
					module.StepInto = false;
					break;
				default:
					throw new InternalError ();
				}
			}
		}

		public void ModuleOperations (Module[] modules, ModuleOperation[] operations)
		{
			DebuggerBackend backend = GetDebuggerBackend ();

			try {
				backend.ModuleManager.Lock ();

				foreach (Module module in modules)
					module_operation (module, operations);
			} finally {
				backend.ModuleManager.UnLock ();
				backend.SymbolTableManager.Wait ();
			}
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.SymbolFile.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		public ISourceBuffer FindFile (string filename)
		{
			DebuggerBackend backend = GetDebuggerBackend ();
			return backend.SourceFileFactory.FindFile (filename);
		}

		public void LoadLibrary (Process process, string filename)
		{
			DebuggerBackend backend = GetDebuggerBackend ();
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				backend.LoadLibrary (process, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}
	}
}

