using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Frontends.CommandLine
{
	public class ProcessHandle
	{
		DebuggerBackend backend;
		ScriptingContext context;	
		IArchitecture arch;
		Process process;

		int id, pid;
		Hashtable registers;

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process)
		{
			this.context = context;
			this.backend = backend;
			this.process = process;
			this.id = process.ID;

			process.FrameChangedEvent += new StackFrameHandler (frame_changed);
			process.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
			process.MethodChangedEvent += new MethodChangedHandler (method_changed);
			process.MethodInvalidEvent += new MethodInvalidHandler (method_invalid);
			process.StateChanged += new StateChangedHandler (state_changed);
			process.TargetOutput += new TargetOutputHandler (inferior_output);
			process.TargetError += new TargetOutputHandler (inferior_error);
			process.DebuggerOutput += new TargetOutputHandler (debugger_output);
			process.DebuggerError += new DebuggerErrorHandler (debugger_error);
		}

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process,
				      int pid)
			: this (context, backend, process)
		{
			if (pid > 0)
				process.SingleSteppingEngine.Attach (pid, true);
			else
				process.SingleSteppingEngine.Run (false, true);
			initialize ();
			process_events ();
		}

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process,
				      string core_file)
			: this (context, backend, process)
		{
			initialize ();
		}

		void initialize ()
		{
			registers = new Hashtable ();
			arch = process.Architecture;

			for (int i = 0; i < arch.RegisterNames.Length; i++) {
				string register = arch.RegisterNames [i];

				registers.Add (register, arch.AllRegisterIndices [i]);
			}
		}

		int current_frame_idx = -1;
		StackFrame current_frame = null;
		Backtrace current_backtrace = null;
		string current_insn = null;
		IMethod current_method = null;
		ISourceBuffer current_buffer = null;
		string[] current_source = null;

		void frame_changed (StackFrame frame)
		{
			current_frame = frame;
			current_frame_idx = -1;
			current_backtrace = null;
			current_insn = null;

			IDisassembler dis = process.Disassembler;
			if (dis != null) {
				TargetAddress address = frame.TargetAddress;
				current_insn = String.Format ("{0:11x}\t{1}", address,
							      dis.DisassembleInstruction (ref address));
			}
		}

		void frames_invalid ()
		{
			current_insn = null;
			current_frame = null;
			current_frame_idx = -1;
			current_backtrace = null;
		}

		void method_changed (IMethod method)
		{
			current_method = method;
			current_source = null;
			if (!method.HasSource)
				return;

			string contents;
			current_buffer = method.Source.SourceBuffer;
			if (current_buffer.HasContents)
				contents = current_buffer.Contents;
			else {
				SourceFile file = context.SourceFactory.FindFile (current_buffer.Name);
				if (file == null)
					return;
				contents = file.Contents;
			}

			current_source = contents.Split ('\n');
		}

		void method_invalid ()
		{
			current_method = null;
			current_source = null;
			current_buffer = null;
		}

		void target_exited ()
		{
			frames_invalid ();
			method_invalid ();
			process = null;
		}

		void state_changed (TargetState state, int arg)
		{
			string frame = "";
			if (current_frame != null)
				frame = String.Format (" at {0}", current_frame);

			switch (state) {
			case TargetState.EXITED:
				if (arg == 0)
					context.Print ("Process @{0} terminated normally.", id);
				else
					context.Print ("Process @{0} exited with exit code {1}.", id, arg);
				target_exited ();
				break;

			case TargetState.STOPPED:
				if (arg != 0)
					context.Print ("Process @{0} received signal {1}{2}.", id, arg, frame);
				else if (!context.IsInteractive)
					break;
				else
					context.Print ("Process @{0} stopped{1}.", id, frame);

				print_source ();

				if (!context.IsInteractive)
					context.Abort ();

				break;
			}
		}

		void print_source ()
		{
			if (current_insn != null)
				context.Print (current_insn);

			if ((current_source == null) || (current_frame == null) || (current_buffer == null))
				return;

			SourceLocation source = current_frame.SourceLocation;
			if ((source == null) || (source.Buffer != current_buffer))
				return;

			string line = current_source [source.Row - 1];
			context.Print (String.Format ("{0,4} {1}", source.Row, line));
		}

		void inferior_output (string line)
		{
			context.PrintInferior (false, line);
		}

		void inferior_error (string line)
		{
			context.PrintInferior (true, line);
		}

		void debugger_output (string line)
		{
			context.Print ("DEBUGGER OUTPUT: {0}", line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			context.Print ("DEBUGGER ERROR: {0}\n{1}", message, e);
		}

		[DllImport("glib-2.0")]
		static extern bool g_main_context_iteration (IntPtr context, bool may_block);

		public void Step (WhichStepCommand which)
		{
			if (process == null)
				throw new ScriptingException ("Process @{0} not running.", id);
			else if (!process.CanRun)
				throw new ScriptingException ("Process @{0} cannot be executed.", id);

			bool ok;
			switch (which) {
			case WhichStepCommand.Continue:
				ok = process.Continue (context.IsSynchronous);
				break;
			case WhichStepCommand.Step:
				ok = process.StepLine (context.IsSynchronous);
				break;
			case WhichStepCommand.Next:
				ok = process.NextLine (context.IsSynchronous);
				break;
			case WhichStepCommand.StepInstruction:
				ok = process.StepInstruction (context.IsSynchronous);
				break;
			case WhichStepCommand.NextInstruction:
				ok = process.NextInstruction (context.IsSynchronous);
				break;
			case WhichStepCommand.Finish:
				ok = process.Finish (context.IsSynchronous);
				break;
			default:
				throw new Exception ();
			}

			if (!ok)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			process_events ();
		}

		void process_events ()
		{
			while (g_main_context_iteration (IntPtr.Zero, false))
				;
		}

		public void Stop ()
		{
			if (process == null)
				throw new ScriptingException ("Process @{0} not running.", id);
			process.Stop ();
		}

		public void Background ()
		{
			if (process == null)
				throw new ScriptingException ("Process @{0} not running.", id);
			else if (!process.CanRun)
				throw new ScriptingException ("Process @{0} cannot be executed.", id);
			else if (!process.IsStopped)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			process.Continue (true, false);
		}

		public IArchitecture Architecture {
			get {
				if (arch == null)
					throw new ScriptingException ("Process @{0} not running.", id);

				return arch;
			}
		}

		public int ID {
			get {
				return id;
			}
		}

		public int CurrentFrameIndex {
			get {
				if (current_frame_idx == -1)
					return 0;

				return current_frame_idx;
			}

			set {
				GetBacktrace ();
				if ((value < 0) || (value >= current_backtrace.Length))
					throw new ScriptingException ("No such frame.");

				current_frame_idx = value;
				current_frame = current_backtrace [current_frame_idx];
			}
		}

		public Backtrace GetBacktrace ()
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			if (current_backtrace != null)
				return current_backtrace;

			current_backtrace = process.GetBacktrace ();

			if (current_backtrace == null)
				throw new ScriptingException ("No stack.");

			return current_backtrace;
		}

		public StackFrame CurrentFrame {
			get {
				return GetFrame (current_frame_idx);
			}
		}

		public StackFrame GetFrame (int number)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			if (number == -1) {
				if (current_frame == null)
					current_frame = process.CurrentFrame;

				return current_frame;
			}

			GetBacktrace ();
			if (number >= current_backtrace.Length)
				throw new ScriptingException ("No such frame: {0}", number);

			return current_backtrace [number];
		}

		public void PrintFrame ()
		{
			PrintFrame (CurrentFrame);
		}

		public void PrintFrame (int number)
		{
			PrintFrame (GetFrame (number));
		}

		public void PrintFrame (StackFrame frame)
		{
			context.Print (frame);
			Disassemble (frame);

			SourceLocation location = frame.SourceLocation;
			if (location == null)
				return;

			IMethod method = frame.Method;
			if ((method == null) || !method.HasSource)
				return;

			IMethodSource source = method.Source;
			if (source == null)
				return;

			string contents;
			ISourceBuffer buffer = source.SourceBuffer;
			if (buffer.HasContents)
				contents = buffer.Contents;
			else {
				SourceFile file = context.SourceFactory.FindFile (buffer.Name);
				if (file == null)
					return;
				contents = file.Contents;
			}

			string[] lines = contents.Split ('\n');
			string line = lines [location.Row - 1];
			context.Print (String.Format ("{0,4} {1}", location.Row, line));
		}

		public long GetRegister (int frame_number, string name)
		{
			StackFrame frame = GetFrame (frame_number);

			if (!registers.Contains (name))
				throw new ScriptingException ("No such register: %{0}", name);

			int register = (int) registers [name];

			Register[] frame_registers = frame.Registers;
			if (frame_registers == null)
				throw new ScriptingException ("Cannot get registers of selected stack frame.");

			foreach (Register reg in frame_registers) {
				if (reg.Index == register)
					return (long) reg.Data;
			}

			throw new ScriptingException ("Cannot get this register from the selected stack frame.");
		}

		public TargetState State {
			get {
				if (process == null)
					return TargetState.NO_TARGET;
				else
					return process.State;
			}
		}

		public Register[] GetRegisters (int frame_number, int[] indices)
		{
			StackFrame frame = GetFrame (frame_number);

			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

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
		
		public int InsertBreakpoint (string method)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (method);
			int index = backend.InsertBreakpoint (breakpoint, (ThreadGroup) process, method);
			if (index < 0)
				throw new ScriptingException ("Could not insert breakpoint.");
			return index;
		}

		public int InsertBreakpoint (string file, int line)
		{
			string full_file = context.GetFullPath (file);
			Breakpoint breakpoint = new SimpleBreakpoint (String.Format ("{0}:{1}", file, line));
			int index = backend.InsertBreakpoint (breakpoint, (ThreadGroup) process, full_file, line);
			if (index < 0)
				throw new ScriptingException ("Could not insert breakpoint.");
			return index;
		}

		public void ShowParameters (int frame_number)
		{
			StackFrame frame = GetFrame (frame_number);

			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] param_vars = frame.Method.Parameters;
			foreach (IVariable var in param_vars)
				PrintVariable (true, var);
		}

		public void ShowLocals (int frame_number)
		{
			StackFrame frame = GetFrame (frame_number);

			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] local_vars = frame.Method.Locals;
			foreach (IVariable var in local_vars)
				PrintVariable (false, var);
		}

		public void PrintVariable (bool is_param, IVariable variable)
		{
			context.Print (variable);
		}

		public IVariable GetVariableInfo (StackFrame frame, string identifier)
		{
			if (frame.Method == null)
				throw new ScriptingException ("Selected stack frame has no method.");

			IVariable[] local_vars = frame.Method.Locals;
			foreach (IVariable var in local_vars) {
				if (var.Name == identifier)
					return var;
			}

			IVariable[] param_vars = frame.Method.Parameters;
			foreach (IVariable var in param_vars) {
				if (var.Name == identifier)
					return var;
			}

			throw new ScriptingException ("No variable of parameter with that name.");
		}

		public ITargetObject GetVariable (int frame_number, string identifier)
		{
			StackFrame frame = GetFrame (frame_number);

			IVariable var = GetVariableInfo (frame, identifier);
			if (!var.IsValid (frame))
				throw new ScriptingException ("Variable out of scope.");

			return var.GetObject (frame);
		}

		public ITargetType GetVariableType (int frame_number, string identifier)
		{
			StackFrame frame = GetFrame (frame_number);

			IVariable var = GetVariableInfo (frame, identifier);
			if (!var.IsValid (frame))
				throw new ScriptingException ("Variable out of scope.");

			return var.Type;
		}

		public void Disassemble (int frame_number)
		{
			Disassemble (GetFrame (frame_number));
		}

		public void Disassemble (StackFrame frame)
		{
			TargetAddress address = frame.TargetAddress;
			TargetAddress old_address = address;
			string disasm = process.DisassembleInstruction (ref address);

			context.Print ("{0:11x}\t{1}", old_address, disasm);
		}

		public void DisassembleMethod (int frame_number)
		{
			DisassembleMethod (GetFrame (frame_number));
		}

		public void DisassembleMethod (StackFrame frame)
		{
			IMethod method = frame.Method;

			if ((method == null) || !method.IsLoaded)
				throw new ScriptingException ("Selected stack frame has no method.");

			TargetAddress address = method.StartAddress;
			while (address < method.EndAddress) {
				TargetAddress old_address = address;
				string disasm = process.DisassembleInstruction (ref address);

				context.Print ("{0:11x}\t{1}", old_address, disasm);
			}
		}

		public override string ToString ()
		{
			return String.Format ("Process @{0}: {1} {2}", id, State, process);
		}
	}

	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public enum WhichStepCommand
	{
		Continue,
		Step,
		Next,
		StepInstruction,
		NextInstruction,
		Finish
	}

	public class ScriptingContext
	{
		ProcessHandle current_process;
		ArrayList procs;

		DebuggerBackend backend;
		DebuggerTextWriter command_output;
		DebuggerTextWriter inferior_output;
		ProcessStart start;
		bool is_synchronous;
		bool is_interactive;
		int exit_code = 0;
		internal static readonly string DirectorySeparatorStr;

		SourceFileFactory source_factory;
		Hashtable scripting_variables;

		static ScriptingContext ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		public ScriptingContext (DebuggerBackend backend, DebuggerTextWriter command_output,
					 DebuggerTextWriter inferior_output, bool is_synchronous,
					 bool is_interactive)
		{
			this.backend = backend;
			this.command_output = command_output;
			this.inferior_output = inferior_output;
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;

			procs = new ArrayList ();
			current_process = null;

			scripting_variables = new Hashtable ();

			foreach (Process process in backend.ThreadManager.Threads) {
				ProcessHandle handle = new ProcessHandle (this, backend, process);
				procs.Add (handle);

				if (process == backend.ThreadManager.MainProcess)
					current_process = handle;
			}

			source_factory = new SourceFileFactory ();

			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
		}

		public SourceFileFactory SourceFactory {
			get { return source_factory; }
		}

		public ProcessStart ProcessStart {
			get { return start; }
			set { start = value; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public bool IsSynchronous {
			get { return is_synchronous; }
		}

		public bool IsInteractive {
			get { return is_interactive; }
		}

		public int ExitCode {
			get { return exit_code; }
			set { exit_code = value; }
		}

		public ProcessHandle[] Processes {
			get {
				ProcessHandle[] retval = new ProcessHandle [procs.Count];
				procs.CopyTo (retval, 0);
				return retval;
			}
		}

		public void Abort ()
		{
			Print ("Caught fatal error while running non-interactively; exiting!");
			Environment.Exit (-1);
		}

		public void Error (string message)
		{
			command_output.WriteLine (true, message);
			if (!IsInteractive)
				Abort ();
		}

		public void Error (string format, params object[] args)
		{
			Error (String.Format (format, args));
		}

		public void Error (ScriptingException ex)
		{
			Error (ex.Message);
		}

		public void Print (string format, params object[] args)
		{
			string message = String.Format (format, args);

			command_output.WriteLine (false, message);
		}

		public void Print (object obj)
		{
			Print ("{0}", obj);
		}

		public void PrintInferior (bool is_stderr, string line)
		{
			inferior_output.WriteLine (is_stderr, line);
		}

		public ProcessHandle CurrentProcess {
			get {
				if (current_process == null)
					throw new ScriptingException ("No target.");

				return current_process;
			}

			set {
				current_process = value;
			}
		}

		public bool HasTarget {
			get {
				return current_process != null;
			}
		}

		public ProcessHandle Start (string[] args)
		{
			return Start (args, -1);
		}

		public ProcessHandle Start (string[] args, int pid)
		{
			if (args.Length == 0)
				throw new ScriptingException ("No program specified.");

			Process process;

			if (args [0] == "core") {
				string [] temp_args = new string [args.Length-1];
				if (args.Length > 1)
					Array.Copy (args, 1, temp_args, 0, args.Length-1);
				args = temp_args;

				start = ProcessStart.Create (null, args, null);
				process = backend.ReadCoreFile (start, "thecore");
				current_process = new ProcessHandle (this, backend, process, "thecore");
			} else {
				start = ProcessStart.Create (null, args, null);
				process = backend.Run (start);
				current_process = new ProcessHandle (this, backend, process, pid);
			}

			procs.Add (current_process);

			return current_process;
		}

		public string GetFullPath (string filename)
		{
			if (start == null)
				return Path.GetFullPath (filename);

			if (Path.IsPathRooted (filename))
				return filename;

			return String.Concat (start.BaseDirectory, DirectorySeparatorStr, filename);
		}

		void thread_created (ThreadManager manager, Process process)
		{
			ProcessHandle handle = new ProcessHandle (this, process.DebuggerBackend, process);
			procs.Add (handle);
		}

		public void ShowVariableType (ITargetType type, string name)
		{
			ITargetArrayType array = type as ITargetArrayType;
			if (array != null) {
				Print ("{0} is an array of {1}", name, array.ElementType);
				return;
			}

			ITargetClassType tclass = type as ITargetClassType;
			ITargetStructType tstruct = type as ITargetStructType;
			if (tclass != null) {
				if (tclass.HasParent)
					Print ("{0} is a class of type {1} which inherits from {2}",
					       name, tclass.Name, tclass.ParentType);
				else
					Print ("{0} is a class of type {1}", name, tclass.Name);
			} else if (tstruct != null)
				Print ("{0} is a value type of type {1}", name, tstruct.Name);

			if (tstruct != null) {
				foreach (ITargetFieldInfo field in tstruct.Fields)
					Print ("  It has a field `{0}' of type {1}", field.Name,
					       field.Type.Name);
				foreach (ITargetFieldInfo property in tstruct.Properties)
					Print ("  It has a property `{0}' of type {1}", property.Name,
					       property.Type.Name);
				foreach (ITargetMethodInfo method in tstruct.Methods)
					Print ("  It has a method: {0}", method);
				return;
			}

			Print ("{0} is a {1}", name, type);
		}

		public VariableExpression this [string identifier] {
			get {
				return (VariableExpression) scripting_variables [identifier];
			}

			set {
				if (scripting_variables.Contains (identifier))
					scripting_variables [identifier] = value;
				else
					scripting_variables.Add (identifier, value);
			}
		}
	}
}
