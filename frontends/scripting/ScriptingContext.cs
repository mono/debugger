using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Backends;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.GetOptions;

namespace Mono.Debugger.Frontends.CommandLine
{
	public delegate void ProcessExitedHandler (ProcessHandle handle);

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
			process.TargetExited += new TargetExitedHandler (target_exited);
			process.DebuggerOutput += new TargetOutputHandler (debugger_output);
			process.DebuggerError += new DebuggerErrorHandler (debugger_error);
		}

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process,
				      int pid)
			: this (context, backend, process)
		{
			if (process.SingleSteppingEngine == null)
				return;

			if (process.SingleSteppingEngine.HasTarget) {
				current_frame = process.SingleSteppingEngine.CurrentFrame;
				if (current_frame.Method != null)
					method_changed (current_frame.Method);
				context.Print ("Process @{0} stopped at {1}.", id, CurrentFrame);
				PrintFrameSource (CurrentFrame);
			} else {
				if (pid > 0)
					process.SingleSteppingEngine.Attach (pid, true);
				else
					process.SingleSteppingEngine.Run (!context.IsSynchronous, true);
			}
			initialize ();
			process_events ();
		}

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process,
				      string core_file)
			: this (context, backend, process)
		{
			initialize ();
		}

		public Process Process {
			get { return process; }
		}

		public event ProcessExitedHandler ProcessExitedEvent;

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
		AssemblerLine current_insn = null;
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
				current_insn = dis.DisassembleInstruction (address);
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

			IMethodSource source = method.Source;
			if (source.SourceFile != null)
				current_buffer = context.SourceFactory.FindFile (source.SourceFile.FileName);
			else
				current_buffer = source.SourceBuffer;

			if (current_buffer == null)
				return;

			current_source = current_buffer.Contents;
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

			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
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

		void print_insn (AssemblerLine line)
		{
			if (line.Label != null)
				context.Print ("{0}:", line.Label);
			context.Print ("{0:11x}\t{1}", line.Address, line.Text);
		}

		void print_source ()
		{
			if (current_insn != null)
				print_insn (current_insn);

			if ((current_source == null) || (current_frame == null) || (current_buffer == null))
				return;

			SourceAddress source = current_frame.SourceAddress;
			if ((source == null) || (source.MethodSource != current_method.Source))
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
			PrintFrameSource (frame);
		}

		public void PrintFrameSource (StackFrame frame)
		{
			Disassemble (frame);

			SourceAddress location = frame.SourceAddress;
			if (location == null)
				return;

			IMethod method = frame.Method;
			if ((method == null) || !method.HasSource)
				return;

			IMethodSource source = method.Source;
			if (source == null)
				return;

			string contents;
			ISourceBuffer buffer;
			if (source.SourceFile != null)
				buffer = context.SourceFactory.FindFile (source.SourceFile.FileName);
			else
				buffer = source.SourceBuffer;

			if (buffer == null)
				return;

			string line = buffer.Contents [location.Row - 1];
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
			Disassemble (frame.TargetAddress);
		}

		public AssemblerLine Disassemble (TargetAddress address)
		{
			AssemblerLine line = process.DisassembleInstruction (address);

			if (line != null)
				print_insn (line);
			else
				context.Error ("Cannot disassemble instruction at address {0}.", address);

			return line;
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
				AssemblerLine line = Disassemble (address);

				if (line != null)
					address += line.InstructionSize;
				else
					break;
			}
		}

		public void Kill ()
		{
			process.Kill ();
			process.Dispose ();
			target_exited ();
		}

		public override string ToString ()
		{
			return String.Format ("Process @{0}: {1} {2} {3}", id, State, process.PID, process);
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
		DontStep,
		ShowBreakpoints
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

	internal enum StartMode
	{
		Unknown,
		CoreFile,
		LoadSession,
		StartApplication
	}

	internal class MyOptions : Options
	{
		public MyOptions()
		{
			ParsingMode = OptionsParsingMode.Linux;
			EndOptionProcessingWithDoubleDash = true;
		}

		StartMode start_mode = StartMode.Unknown;

		[Option("PARAM is one of `core' (to load a core file),\n\t\t\t" +
			"`load' (to load a previously saved debugging session)\n\t\t\t" +
			"or `start' (to start a new application).", 'm')]
		public WhatToDoNext mode (string value)
		{
			if (start_mode != StartMode.Unknown) {
				Console.WriteLine ("This argument cannot be used multiple times.");
				return WhatToDoNext.AbandonProgram;
			}

			switch (value) {
			case "core":
				start_mode = StartMode.CoreFile;
				return WhatToDoNext.GoAhead;

			case "load":
				start_mode = StartMode.LoadSession;
				return WhatToDoNext.GoAhead;

			case "start":
				start_mode = StartMode.StartApplication;
				return WhatToDoNext.GoAhead;

			default:
				Console.WriteLine ("Invalid `--mode' argument.");
				return WhatToDoNext.AbandonProgram;
			}
		}

		[Option("The command-line prompt", 'p')]
		public string prompt = "$";

		public StartMode StartMode {
			get { return start_mode; }
		}

		[Option("Display version and licensing information", 'V', "version")]
		public override WhatToDoNext DoAbout()
		{
			base.DoAbout ();
			return WhatToDoNext.AbandonProgram;
		}
	}

	public class ScriptingContext
	{
		ProcessHandle current_process;
		ArrayList procs;

		DebuggerBackend backend;
		DebuggerTextWriter command_output;
		DebuggerTextWriter inferior_output;
		ProcessStart start;
		string prompt = "$";
		bool is_synchronous;
		bool is_interactive;
		int exit_code = 0;
		internal static readonly string DirectorySeparatorStr;

		MyOptions options;

		ArrayList method_search_results;
		SourceFileFactory source_factory;
		Hashtable scripting_variables;
		Module[] modules;

		static ScriptingContext ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		public ScriptingContext (DebuggerTextWriter command_out, DebuggerTextWriter inferior_out,
					 bool is_synchronous, bool is_interactive)
		{
			this.command_output = command_out;
			this.inferior_output = inferior_out;
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;

			options = new MyOptions ();

			procs = new ArrayList ();
			current_process = null;

			scripting_variables = new Hashtable ();
			method_search_results = new ArrayList ();

			source_factory = new SourceFileFactory ();
		}

		protected void Initialize ()
		{
			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
			backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
		}

		public SourceFileFactory SourceFactory {
			get { return source_factory; }
		}

		public ProcessStart ProcessStart {
			get { return start; }
		}

		public DebuggerBackend DebuggerBackend {
			get {
				if (backend != null)
					return backend;

				throw new ScriptingException ("No backend loaded.");
			}
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

		public string Prompt {
			get {
				return prompt;
			}
		}

		public Process Start (string[] args)
		{
			backend = ParseArguments (args);
			if (backend == null)
				return null;

			return Run ();
		}

		public DebuggerBackend ParseArguments (string[] args)
		{
			if (backend != null)
				throw new ScriptingException ("Already have a target.");
			if (args.Length == 0)
				throw new ScriptingException ("No program specified.");

			options.ProcessArgs (args);
			prompt = options.prompt;

			switch (options.StartMode) {
			case StartMode.CoreFile:
				if (options.RemainingArguments.Length < 2)
					throw new ScriptingException (
						"You need to specify at least the name of the core " +
						"file and the application it was generated from.");
				return LoadCoreFile (options.RemainingArguments);

			case StartMode.LoadSession:
				if (options.RemainingArguments.Length != 1)
					throw new ScriptingException (
						"This mode requires exactly one argument, the file " +
						"to load the session from.");
				return LoadSession (options.RemainingArguments [0]);

			case StartMode.Unknown:
				if (options.RemainingArguments.Length == 0)
					return null;
				return StartApplication (options.RemainingArguments);

			default:
				return StartApplication (options.RemainingArguments);
			}
		}

		protected DebuggerBackend LoadCoreFile (string[] args)
		{
			backend = new DebuggerBackend ();
			Initialize ();

			string core_file = args [0];
			string [] temp_args = new string [args.Length-1];
			Array.Copy (args, 1, temp_args, 0, args.Length-1);
			args = temp_args;

			start = ProcessStart.Create (null, args, null, core_file);
			return backend;
		}

		protected DebuggerBackend StartApplication (string[] args)
		{
			backend = new DebuggerBackend ();
			Initialize ();

			start = ProcessStart.Create (null, args, null);
			return backend;
		}

		protected DebuggerBackend LoadSession (string filename)
		{
			StreamingContext context = new StreamingContext (StreamingContextStates.All, this);
			BinaryFormatter formatter = new BinaryFormatter (null, context);

			using (FileStream stream = new FileStream (filename, FileMode.Open)) {
				backend = (DebuggerBackend) formatter.Deserialize (stream);
			}

			Initialize ();

			start = backend.ProcessStart;
			return backend;
		}

		public Process Run ()
		{
			if (current_process != null)
				throw new ScriptingException ("Process already started.");

			Process process = backend.Run (start);
			current_process = new ProcessHandle (this, backend, process, -1);

			add_process (current_process);

			return process;
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
			if (process.State == TargetState.DAEMON)
				return;
			ProcessHandle handle = new ProcessHandle (this, process.DebuggerBackend, process);
			add_process (handle);
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

		void modules_changed ()
		{
			modules = backend.Modules;
		}

		public void ShowBreakpoints (Module module)
		{
			BreakpointHandle[] breakpoints = module.BreakpointHandles;
			if (breakpoints.Length == 0)
				return;

			Print ("Breakpoints for module {0}:", module.Name);
			foreach (BreakpointHandle handle in breakpoints) {
				Print ("{0} ({1}): {2}", handle.Breakpoint.Index,
				       handle.ThreadGroup.Name, handle.Breakpoint);
			}
		}

		public void ShowBreakpoints ()
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			foreach (Module module in modules)
				ShowBreakpoints (module);
		}

		public Breakpoint FindBreakpoint (int index, out Module out_module)
		{
			if (modules == null)
				goto error;

			foreach (Module module in modules) {
				foreach (Breakpoint breakpoint in module.Breakpoints) {
					if (breakpoint.Index == index) {
						out_module = module;
						return breakpoint;
					}
				}
			}

		error:
			out_module = null;
			throw new ScriptingException ("No such breakpoint.");
		}

		public Breakpoint FindBreakpoint (int index)
		{
			Module module;
			return FindBreakpoint (index, out module);
		}

		public void DeleteBreakpoint (int index)
		{
			Module module;
			FindBreakpoint (index, out module);
			module.RemoveBreakpoint (index);
		}

		public void ShowModules ()
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			for (int i = 0; i < modules.Length; i++) {
				Module module = modules [i];

				Print ("{0,4} {1}{2}{3}{4}{5}", i, module.Name,
				       module.IsLoaded ? " loaded" : "",
				       module.SymbolsLoaded ? " symbols" : "",
				       module.StepInto ? " step" : "",
				       module.LoadSymbols ? "" :  " ignore");
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
				case ModuleOperation.ShowBreakpoints:
					ShowBreakpoints (module);
					break;
				default:
					throw new InternalError ();
				}
			}
		}

		public void ModuleOperations (int[] module_indices, ModuleOperation[] operations)
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			backend.ModuleManager.Lock ();

			foreach (int index in module_indices) {
				if ((index < 0) || (index > modules.Length)) {
					Error ("No such module {0}.", index);
					return;
				}

				module_operation (modules [index], operations);
			}

			backend.ModuleManager.UnLock ();
			backend.SymbolTableManager.Wait ();
		}

		public void ModuleOperations (ModuleOperation[] operations)
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			foreach (Module module in modules)
				module_operation (module, operations);
		}

		public void ShowSources (int[] module_indices)
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			backend.ModuleManager.Lock ();

			if (module_indices.Length == 0) {
				foreach (Module module in modules)
					ShowSources (module);
			} else {
				foreach (int index in module_indices) {
					if ((index < 0) || (index > modules.Length)) {
						Error ("No such module {0}.", index);
						return;
					}

					ShowSources (modules [index]);
				}
			}

			backend.ModuleManager.UnLock ();
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.Sources)
				Print ("  {0}", source);
		}

		void process_exited (ProcessHandle process)
		{
			procs.Remove (process);
			if (process == current_process)
				current_process = null;
		}

		void add_process (ProcessHandle process)
		{
			process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
			procs.Add (process);
		}

		public void Save (string filename)
		{
			StreamingContext context = new StreamingContext (StreamingContextStates.All, this);
			BinaryFormatter formatter = new BinaryFormatter (null, context);

			using (FileStream stream = new FileStream (filename, FileMode.Create)) {
				formatter.Serialize (stream, backend);
			}
		}

		public ProcessHandle ProcessByID (int number)
		{
			if (number == -1)
				return CurrentProcess;

			foreach (ProcessHandle proc in Processes)
				if (proc.ID == number)
					return proc;

			throw new ScriptingException ("No such process: {0}", number);
		}

		public void ShowThreadGroups ()
		{
			foreach (ThreadGroup group in ThreadGroup.ThreadGroups) {
				StringBuilder ids = new StringBuilder ();
				foreach (IProcess thread in group.Threads) {
					ids.Append (" @");
					ids.Append (thread.ID);
				}
				Print ("{0}:{1}", group.Name, ids.ToString ());
			}
		}

		public void CreateThreadGroup (string name, int[] threads)
		{
			if (ThreadGroup.ThreadGroupExists (name))
				throw new ScriptingException ("A thread group with that name already exists.");

			AddToThreadGroup (name, threads);
		}

		public void AddToThreadGroup (string name, int[] threads)
		{
			ThreadGroup group = ThreadGroup.CreateThreadGroup (name);

			if (group.IsSystem)
				throw new ScriptingException ("Cannot modify system-created thread group.");

			foreach (int thread in threads) {
				ProcessHandle process = ProcessByID (thread);
				group.AddThread (process.Process);
			}
		}

		public void RemoveFromThreadGroup (string name, int[] threads)
		{
			ThreadGroup group = ThreadGroup.CreateThreadGroup (name);
	
			if (group.IsSystem)
				throw new ScriptingException ("Cannot modify system-created thread group.");

			foreach (int thread in threads) {
				ProcessHandle process = ProcessByID (thread);
				group.RemoveThread (process.Process);
			}
		}

		public int InsertBreakpoint (ThreadGroup group, SourceLocation location)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (location.Name);
			int index = backend.InsertBreakpoint (breakpoint, group, location);
			if (index < 0)
				throw new ScriptingException ("Could not insert breakpoint.");
			return index;
		}

		public SourceMethod FindMethod (string file, int line)
		{
			if (modules == null)
				throw new ScriptingException ("No modules.");

			string full_file = GetFullPath (file);
			foreach (Module module in modules) {
				SourceMethod method = module.FindMethod (full_file, line);
				
				if (method != null)
					return method;
			}

			throw new ScriptingException ("No method contains the specified file/line.");
		}

		public SourceMethod FindMethod (string name)
		{
			if (modules == null)
				throw new ScriptingException ("No modules.");

			foreach (Module module in modules) {
				SourceMethod method = module.FindMethod (name);
				
				if (method != null)
					return method;
			}

			throw new ScriptingException ("No such method.");
		}

		public void AddMethodSearchResult (SourceMethod[] methods)
		{
			Print ("More than one method matches your query:");
			foreach (SourceMethod method in methods) {
				Print ("{0,4}  {1}", method_search_results.Count + 1, method.Name);
				method_search_results.Add (method);
			}
		}

		public SourceMethod GetMethodSearchResult (int index)
		{
			if ((index < 1) || (index > method_search_results.Count))
				throw new ScriptingException ("No such history item.");

			return (SourceMethod) method_search_results [index - 1];
		}
	}
}
