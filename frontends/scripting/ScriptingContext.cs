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
		Process process;

		static int next_id = 0;
		int pid, id;

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend, Process process)
		{
			this.context = context;
			this.backend = backend;
			this.process = process;
			this.id = ++next_id;

			process.FrameChangedEvent += new StackFrameHandler (frame_changed);
			process.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
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
				process.SingleSteppingEngine.Run (true);
			process_events ();
		}

		int current_frame_idx = -1;
		StackFrame current_frame = null;
		StackFrame[] current_backtrace = null;
		string current_insn = null;

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
				break;

			case TargetState.STOPPED:
				if (arg != 0)
					context.Print ("Process @{0} received signal {1}{2}.", id, arg, frame);
				else
					context.Print ("Process @{0} stopped{1}.", id, frame);

				if (current_insn != null)
					context.Print (current_insn);
				break;
			}
		}

		void inferior_output (string line)
		{
			context.Print ("INFERIOR OUTPUT: {0}", line);
		}

		void inferior_error (string line)
		{
			context.Print ("INFERIOR ERROR: {0}", line);
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
				if (process == null)
					throw new ScriptingException ("Process @{0} not running.", id);

				return process.Architecture;
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

		public StackFrame[] GetBacktrace ()
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (State != TargetState.STOPPED)
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
			else if (State != TargetState.STOPPED)
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

		public TargetState State {
			get {
				if (process == null)
					return TargetState.NO_TARGET;
				else
					return process.SingleSteppingEngine.State;
			}
		}

		public long[] GetRegisters (int[] registers)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (State != TargetState.STOPPED)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			return process.GetRegisters (registers);
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
		TextWriter stdout;
		TextWriter stderr;
		bool is_synchronous;

		public ScriptingContext (DebuggerBackend backend, TextWriter stdout, TextWriter stderr,
					 bool is_synchronous)
		{
			this.backend = backend;
			this.stdout = stdout;
			this.stderr = stderr;
			this.is_synchronous = is_synchronous;

			procs = new ArrayList ();
			current_process = null;

			foreach (Process process in backend.ThreadManager.Threads) {
				ProcessHandle handle = new ProcessHandle (this, backend, process);
				procs.Add (handle);
				if (current_process == null)
					current_process = handle;
			}

			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
		}

		public bool IsSynchronous {
			get { return is_synchronous; }
		}

		public ProcessHandle[] Processes {
			get {
				ProcessHandle[] retval = new ProcessHandle [procs.Count];
				procs.CopyTo (retval, 0);
				return retval;
			}
		}

		public void Error (string format, params object[] args)
		{
			string message = String.Format (format, args);

			stderr.WriteLine ("ERROR: {0}", message);
		}

		public void Error (ScriptingException ex)
		{
			stderr.WriteLine (ex.Message);
		}

		public void Print (string format, params object[] args)
		{
			stdout.WriteLine (format, args);
		}

		public void Print (object obj)
		{
			Print ("{0}", obj);
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

		public ProcessHandle Start (string[] args, int pid)
		{
			if (args.Length == 0)
				throw new ScriptingException ("No program specified.");

			ProcessStart start;
			Process process;

			if (args [0] == "core") {
				string [] temp_args = new string [args.Length-1];
				if (args.Length > 1)
					Array.Copy (args, 1, temp_args, 0, args.Length-1);
				args = temp_args;

				start = ProcessStart.Create (null, args, null);
				process = backend.ReadCoreFile (start, "thecore");
			} else{
				start = ProcessStart.Create (null, args, null);
				process = backend.Run (start);
			}

			current_process = new ProcessHandle (this, backend, process, pid);
			procs.Add (current_process);

			return current_process;
		}

		void thread_created (ThreadManager manager, Process process)
		{
			ProcessHandle handle = new ProcessHandle (this, process.DebuggerBackend, process);
			procs.Add (handle);
		}
	}
}
