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
		int id;

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

			running = true;
			process.SingleSteppingEngine.Run ();
			wait_until_stopped ();
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
			// Console.WriteLine ("STATE CHANGED: {0} {1}", state, arg);
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
			if (process == null)
				throw new ScriptingException ("Process @{0} not running.", id);
			else if (!process.CanRun)
				throw new ScriptingException ("Process @{0} cannot be executed.", id);
			else if (!process.IsStopped)
				throw new ScriptingException ("Process @{0} is not stopped.", id);

			running = true;

			switch (which) {
			case WhichStepCommand.Continue:
				process.Continue ();
				break;
			case WhichStepCommand.Step:
				process.StepLine ();
				break;
			case WhichStepCommand.Next:
				process.NextLine ();
				break;
			case WhichStepCommand.StepInstruction:
				process.StepInstruction ();
				break;
			case WhichStepCommand.NextInstruction:
				process.NextInstruction ();
				break;
			case WhichStepCommand.Finish:
				process.Finish ();
				break;
			default:
				throw new Exception ();
			}

			wait_until_stopped ();
		}

		void wait_until_stopped ()
		{
			while (g_main_context_iteration (IntPtr.Zero, false))
				;

			while (running)
				g_main_context_iteration (IntPtr.Zero, true);

			while (g_main_context_iteration (IntPtr.Zero, false))
				;
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

			if (number == -1) {
				if (current_frame == null)
					current_frame = process.CurrentFrame;

				Console.WriteLine ("FRAME: {0}", process.CurrentFrameAddress);

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
					return process.State;
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

		public ScriptingContext ()
		{
			procs = new ArrayList ();
			current_process = null;
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

			current_process = new ProcessHandle (this, backend, process);
			procs.Add (current_process);

			return current_process;
		}
	}
}
