using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.GetOptions;

namespace Mono.Debugger.Frontends.Scripting
{
	public delegate void ProcessExitedHandler (ProcessHandle handle);

	public class FrameHandle
	{
		ProcessHandle process;
		StackFrame frame;

		public FrameHandle (ProcessHandle process, StackFrame frame)
		{
			this.process = process;
			this.frame = frame;
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
			Disassemble (context, frame.TargetAddress);
		}

		AssemblerLine Disassemble (ScriptingContext context, TargetAddress address)
		{
			AssemblerLine line = frame.DisassembleInstruction (address);

			if (line != null)
				context.PrintInstruction (line);
			else
				context.Error ("Cannot disassemble instruction at address {0}.", address);

			return line;
		}

		public void DisassembleMethod (ScriptingContext context)
		{
			IMethod method = frame.Method;

			if ((method == null) || !method.IsLoaded)
				throw new ScriptingException ("Selected stack frame has no method.");

			AssemblerMethod asm = frame.DisassembleMethod ();
			foreach (AssemblerLine line in asm.Lines)
				context.PrintInstruction (line);
		}

		public int[] RegisterIndices {
			get { return process.Architecture.RegisterIndices; }
		}

		public string[] RegisterNames {
			get { return process.Architecture.RegisterNames; }
		}

		public Register[] GetRegisters (int[] indices)
		{
			Register[] registers = frame.Registers;
			if (registers == null)
				throw new ScriptingException ("Cannot get registers of selected stack frame.");

			if (indices == null)
				return registers;

			ArrayList list = new ArrayList ();
			for (int i = 0; i < indices.Length; i++) {
				foreach (Register register in registers) {
					if (register.Index == indices [i]) {
						list.Add (register);
						break;
					}
				}
			}

			Register[] retval = new Register [list.Count];
			list.CopyTo (retval, 0);
			return retval;	
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
			TargetLocation location = GetRegisterLocation (index, offset, false);
			return type.GetObject (location);
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
				context.Interpreter.Style.PrintVariable (var, frame);
		}

		public void ShowLocals (ScriptingContext context)
		{
			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] local_vars = frame.Locals;
			foreach (IVariable var in local_vars)
				context.Interpreter.Style.PrintVariable (var, frame);
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

	public class ProcessHandle
	{
		Interpreter interpreter;
		ThreadGroup tgroup;
		Process process;
		string name;
		int id;

		Hashtable registers;

		public ProcessHandle (Interpreter interpreter, Process process)
		{
			this.interpreter = interpreter;
			this.process = process;
			this.name = process.Name;
			this.id = process.ID;

			if (!process.IsDaemon)
				process.TargetEvent += new TargetEventHandler (target_event);
			process.TargetExitedEvent += new TargetExitedHandler (target_exited);
			process.DebuggerOutput += new DebuggerOutputHandler (debugger_output);
			process.DebuggerError += new DebuggerErrorHandler (debugger_error);

			initialize ();
		}

		public ProcessHandle (Interpreter interpreter, Process process, int pid)
			: this (interpreter, process)
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
		}

		int current_frame_idx = -1;
		FrameHandle current_frame = null;
		BacktraceHandle current_backtrace = null;
		AssemblerLine current_insn = null;

		void target_exited ()
		{
			process = null;

			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
		}

		void target_event (object sender, TargetEventArgs args)
		{
			if (args.Frame != null) {
				current_frame = new FrameHandle (this, args.Frame);
				current_frame_idx = -1;
				current_backtrace = null;

				current_insn = args.Frame.DisassembleInstruction (args.Frame.TargetAddress);
			} else {
				current_insn = null;
				current_frame = null;
				current_frame_idx = -1;
				current_backtrace = null;
			}

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

			case TargetEventType.TargetExited:
				if ((int) args.Data == 0)
					interpreter.Print ("{0} terminated normally.", Name);
				else
					interpreter.Print ("{0} exited with exit code {1}.",
							   id, (int) args.Data);
				target_exited ();
				break;

			case TargetEventType.TargetSignaled:
				interpreter.Print ("{0} died with fatal signal {1}.",
						   id, (int) args.Data);
				target_exited ();
				break;
			}
		}

		void debugger_output (string line)
		{
			interpreter.Print ("DEBUGGER OUTPUT: {0}", line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			interpreter.Print ("DEBUGGER ERROR: {0}\n{1}", message, e);
		}

		public void Step (WhichStepCommand which)
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			else if (!process.CanRun)
				throw new ScriptingException ("{0} cannot be executed.", Name);

			bool ok;
			switch (which) {
			case WhichStepCommand.Continue:
				ok = process.Continue (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.Step:
				interpreter.Style.IsNative = false;
				ok = process.StepLine (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.Next:
				interpreter.Style.IsNative = false;
				ok = process.NextLine (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.StepInstruction:
				interpreter.Style.IsNative = true;
				ok = process.StepInstruction (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.StepNativeInstruction:
				interpreter.Style.IsNative = true;
				ok = process.StepNativeInstruction (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.NextInstruction:
				interpreter.Style.IsNative = true;
				ok = process.NextInstruction (interpreter.IsSynchronous);
				break;
			case WhichStepCommand.Finish:
				ok = process.Finish (interpreter.IsSynchronous);
				break;
			default:
				throw new Exception ();
			}

			if (!ok)
				throw new ScriptingException ("{0} is not stopped.", Name);
		}

		public void Stop ()
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			process.Stop ();
		}

		public void Background ()
		{
			if (process == null)
				throw new ScriptingException ("{0} not running.", Name);
			else if (!process.CanRun)
				throw new ScriptingException ("{0} cannot be executed.", Name);
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			process.Continue (true, false);
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
			target_exited ();
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
	}

	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
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
		Finish
	}

	public class ScriptingContext
	{
		ProcessHandle current_process;
		int current_frame_idx = -1;
		Interpreter interpreter;

		ScriptingContext parent;
		ArrayList method_search_results;
		Hashtable method_search_hash;

		bool is_interactive;
		bool is_synchronous;

		internal ScriptingContext (Interpreter interpreter, bool is_interactive,
					   bool is_synchronous)
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
				formatted = interpreter.Style.FormatObject (obj);
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
			} catch (Exception ex) {
				Console.WriteLine ("EX: {0}", ex);
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

		public void AddMethodSearchResult (SourceMethod[] methods)
		{
			interpreter.Print ("More than one method matches your query:");
			PrintMethods (methods);
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

		int last_line = -1;
		string[] current_source_code = null;

		public void ListSourceCode (SourceLocation location, int count)
		{
			int start;
			if ((location == null) && (current_source_code == null))
				location = CurrentLocation;
			if (location == null) {
				start = last_line;
			} else {
				string filename = location.SourceFile.FileName;
				ISourceBuffer buffer = interpreter.FindFile (filename);
				if (buffer == null)
					throw new ScriptingException (
						"Cannot find source file `{0}'", filename);

				current_source_code = buffer.Contents;
				start = Math.Max (location.Line - 2, 0);
			}

			last_line = Math.Min (start + count, current_source_code.Length);

			for (int line = start; line < last_line; line++)
				interpreter.Print (String.Format ("{0,4} {1}", line, current_source_code [line]));
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
			long dynamic = obj.Type.HasFixedSize ? -1 : obj.DynamicSize;
			return String.Format ("object:{0}:{1}:{2}", obj.IsValid,
					      dynamic, DumpType (obj.Type));
		}

		public string DumpType (ITargetType type)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (type.Name);
			sb.Append (":");
			sb.Append (type.HasFixedSize);
			sb.Append (":");
			sb.Append (type.Size);
			sb.Append (":");
			sb.Append (type.Kind);
			sb.Append (" ");

			switch (type.Kind) {
			case TargetObjectKind.Fundamental:
				sb.Append (((ITargetFundamentalType) type).Type);
				break;

			case TargetObjectKind.Pointer: {
				ITargetPointerType ptype = (ITargetPointerType) type;
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
				sb.Append (((ITargetArrayType) type).ElementType.Name);
				break;

			case TargetObjectKind.Alias: {
				ITargetTypeAlias alias = (ITargetTypeAlias) type;
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
	}
}

