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

		static int next_id = 0;
		int id;

		public ProcessHandle (ScriptingContext context, DebuggerBackend backend)
		{
			this.context = context;
			this.backend = backend;
			this.id = ++next_id;

			backend.FrameChangedEvent += new StackFrameHandler (frame_changed);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
			backend.StateChanged += new StateChangedHandler (state_changed);
			backend.TargetOutput += new TargetOutputHandler (inferior_output);
			backend.TargetError += new TargetOutputHandler (inferior_error);
			backend.DebuggerOutput += new TargetOutputHandler (debugger_output);
			backend.DebuggerError += new DebuggerErrorHandler (debugger_error);
		}

		int current_frame_idx = -1;
		StackFrame current_frame = null;
		StackFrame[] current_backtrace = null;
		bool running = false;

		void frame_changed (StackFrame frame)
		{
			current_frame = frame;
			current_frame_idx = -1;
			current_backtrace = null;

			context.Print ("Process @{0} stopped at {1}", id, frame);
		}

		void frames_invalid ()
		{
			current_frame = null;
			current_frame_idx = -1;
			current_backtrace = null;
		}

		void state_changed (TargetState state, int arg)
		{
			switch (state) {
			case TargetState.EXITED:
				if (arg == 0)
					context.Print ("Process @{0} terminated normally.", id);
				else
					context.Print ("Process @{0} exited with exit code {1}.", id, arg);
				running = false;
				break;

			case TargetState.STOPPED:
				if (arg != 0)
					context.Print ("Process @{0} received signal {1}.", id, arg);
				running = false;
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
			if (which == WhichStepCommand.Run) {
				if (SSE != null)
					throw new ScriptingException ("Process @{0} already running.", id);
			} else if (SSE == null)
				throw new ScriptingException ("Process @{0} not running.", id);

			running = true;

			switch (which) {
			case WhichStepCommand.Run:
				Run ();
				break;
			case WhichStepCommand.Continue:
				SSE.Continue ();
				break;
			case WhichStepCommand.Step:
				SSE.StepLine ();
				break;
			case WhichStepCommand.Next:
				SSE.NextLine ();
				break;
			case WhichStepCommand.StepInstruction:
				SSE.StepInstruction ();
				break;
			case WhichStepCommand.NextInstruction:
				SSE.NextInstruction ();
				break;
			default:
				throw new Exception ();
			}

			while (g_main_context_iteration (IntPtr.Zero, false))
				;

			while (running)
				g_main_context_iteration (IntPtr.Zero, true);
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
			if (current_backtrace != null)
				return current_backtrace;

			if (current_frame != null)
				current_backtrace = backend.GetBacktrace ();

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
			if (backend.State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");

			if (number == -1)
				return current_frame;

			GetBacktrace ();
			if (number >= current_backtrace.Length)
				throw new ScriptingException ("No such frame: {0}", number);

			return current_backtrace [number];
		}

		public void Run ()
		{
			backend.Run ();
		}

		public SingleSteppingEngine SSE {
			get {
				return backend.SingleSteppingEngine;
			}
		}

		public override string ToString ()
		{
			return String.Format ("Process @{0}: {1} {2}", id, backend.State, backend);
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
		Run,
		Continue,
		Step,
		Next,
		StepInstruction,
		NextInstruction
	}

	public class ScriptingContext
	{
		ProcessHandle current_process;
		ArrayList procs;

		public ScriptingContext ()
		{
			procs = new ArrayList ();
			current_process = null;
		}

		public void Step (ProcessHandle process, WhichStepCommand which)
		{
			process.Step (which);
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

			Console.WriteLine ("ERROR: {0}", message);
		}

		public void Error (ScriptingException ex)
		{
			Console.WriteLine (ex.Message);
		}

		public void Print (string format, params object[] args)
		{
			Console.WriteLine (format, args);
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

		public ProcessHandle Start (string[] args)
		{
			if (args.Length == 0)
				throw new ScriptingException ("No program specified.");

			DebuggerBackend backend = new DebuggerBackend ();
			current_process = new ProcessHandle (this, backend);
			procs.Add (current_process);

			if (args [0] == "core") {
				string [] program_args = new string [args.Length-2];
				if (args.Length > 2)
					Array.Copy (args, 2, program_args, 0, args.Length-2);

				backend.CommandLineArguments = program_args;
				backend.TargetApplication = args [1];
				backend.ReadCoreFile ("thecore");
			} else{
				string [] program_args = new string [args.Length-1];
				if (args.Length > 1)
					Array.Copy (args, 1, program_args, 0, args.Length-1);

				backend.CommandLineArguments = program_args;
				backend.TargetApplication = args [0];
				Step (current_process, WhichStepCommand.Run);
			}

			return current_process;
		}
	}
}
