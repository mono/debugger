using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Frontends.CommandLine
{
	public class Process
	{
		DebuggerBackend backend;

		static int next_id = 0;
		int id;

		public Process (DebuggerBackend backend)
		{
			this.backend = backend;
			this.id = ++next_id;

			backend.FrameChangedEvent += new StackFrameHandler (frame_changed);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
		}

		int current_frame_idx = -1;
		StackFrame current_frame = null;
		StackFrame[] current_backtrace = null;

		void frame_changed (StackFrame frame)
		{
			current_frame = frame;
			current_frame_idx = -1;
			current_backtrace = null;
		}

		void frames_invalid ()
		{
			current_frame = null;
			current_frame_idx = -1;
			current_backtrace = null;
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

	public class ScriptingContext
	{
		Process current_process = null;
		ArrayList procs = new ArrayList ();

		public ScriptingContext ()
		{
		}

		public Process[] Processes {
			get {
				Process[] retval = new Process [procs.Count];
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

		public Process CurrentProcess {
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

		public Process Start (string program, string[] args)
		{
			DebuggerBackend backend = new DebuggerBackend ();

			backend.CommandLineArguments = args;
			backend.TargetApplication = program;

			current_process = new Process (backend);
			procs.Add (current_process);
			return current_process;
		}
	}
}
