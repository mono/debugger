using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public class Process : ITargetNotification, IDisposable
	{
		DebuggerBackend backend;
		ProcessStart start;
		BfdContainer bfd_container;

		SingleSteppingEngine sse;
		CoreFile core;
		IInferior inferior;

		internal Process (DebuggerBackend backend, ProcessStart start, BfdContainer bfd_container)
		{
			this.backend = backend;
			this.start = start;
			this.bfd_container = bfd_container;

			inferior = new PTraceInferior (backend, start, bfd_container,
						       new DebuggerErrorHandler (debugger_error));

			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.DebuggerOutput += new TargetOutputHandler (debugger_output);
			inferior.DebuggerError += new DebuggerErrorHandler (debugger_error);

			sse = new SingleSteppingEngine (backend, this, inferior, start.IsNative);

			sse.StateChangedEvent += new StateChangedHandler (target_state_changed);
			sse.MethodInvalidEvent += new MethodInvalidHandler (method_invalid);
			sse.MethodChangedEvent += new MethodChangedHandler (method_changed);
			sse.FrameChangedEvent += new StackFrameHandler (frame_changed);
			sse.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
		}

		internal Process (DebuggerBackend backend, ProcessStart start, BfdContainer bfd_container,
				  string core_file)
		{
			this.backend = backend;

			core = new CoreFileElfI386 (backend, start.TargetApplication,
						    core_file, bfd_container);
			inferior = core;
		}

		public DebuggerBackend DebuggerBackend {
			get {
				check_disposed ();
				return backend;
			}
		}

		public SingleSteppingEngine SingleSteppingEngine {
			get {
				check_disposed ();
				return sse;
			}
		}

		public IInferior Inferior {
			get {
				check_disposed ();
				return inferior;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_disposed ();				
				return inferior;
			}
		}

		public IDisassembler Disassembler {
			get {
				check_disposed ();
				if (inferior == null)
					return null;

				return inferior.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_disposed ();
				if (inferior == null)
					return null;
				
				return inferior.Architecture;
			}
		}

		//
		// ITargetNotification
		//

		bool busy = false;
		public TargetState State {
			get {
				if (busy)
					return TargetState.BUSY;
				else if (sse != null)
					return sse.State;
				else if (inferior != null)
					return inferior.State;
				else
					return TargetState.NO_TARGET;
			}
		}

		bool DebuggerBusy {
			get {
				return busy;
			}

			set {
				if (busy == value)
					return;

				busy = value;
				if (StateChanged != null)
					StateChanged (State, 0);
			}
		}

		void target_state_changed (TargetState new_state, int arg)
		{
			if (new_state == TargetState.STOPPED) {
				if (busy) {
					busy = false;
					return;
				}
			}

			if (new_state == TargetState.BUSY) {
				busy = true;
				return;
			}

			busy = false;

			if (StateChanged != null)
				StateChanged (new_state, arg);
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event TargetOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;
		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		void inferior_output (string line)
		{
			if (TargetOutput != null)
				TargetOutput (line);
		}

		void inferior_errors (string line)
		{
			if (TargetError != null)
				TargetError (line);
		}

		void debugger_output (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			if (DebuggerError != null)
				DebuggerError (this, message, e);
		}

		void method_invalid ()
		{
			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
		}

		void method_changed (IMethod method)
		{
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}

		void frame_changed (StackFrame frame)
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		void frames_invalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		// <summary>
		//   If true, we have a target.
		// </summary>
		public bool HasTarget {
			get { return inferior != null; }
		}

		// <summary>
		//   If true, we have a target which can be executed (ie. it's not a core file).
		// </summary>
		public bool CanRun {
			get { return HasTarget && sse != null; }
		}

		// <summary>
		//   If true, we have a target which can be executed and it is currently stopped
		//   so that we can issue a step command.
		// </summary>
		public bool CanStep {
			get { return CanRun && sse.State == TargetState.STOPPED; }
		}

		// <summary>
		//   If true, the target is currently stopped and thus its memory/registers can
		//   be read/writtern.
		// </summary>
		public bool IsStopped {
			get { return State == TargetState.STOPPED || State == TargetState.CORE_FILE; }
		}

		public void StepInstruction ()
		{
			check_can_run ();
			sse.StepInstruction ();
		}

		public void NextInstruction ()
		{
			check_can_run ();
			sse.NextInstruction ();
		}

		public void StepLine ()
		{
			check_can_run ();
			sse.StepLine ();
		}

		public void NextLine ()
		{
			check_can_run ();
			sse.NextLine ();
		}

		public void Continue ()
		{
			check_can_run ();
			sse.Continue (false);
		}

		public void Continue (bool in_background)
		{
			check_can_run ();
			sse.Continue (in_background);
		}

		public void Continue (TargetAddress until)
		{
			check_can_run ();
			
			TargetAddress current = inferior.CurrentFrame;

			Console.WriteLine (String.Format ("Requested to run from {0:x} until {1:x}.",
							  current, until));

			while (current < until)
				current += inferior.Disassembler.GetInstructionSize (current);

			if (current != until)
				Console.WriteLine (String.Format (
					"Oooops: reached {0:x} but symfile had {1:x}",
					current, until));

			sse.Continue (until);
		}

		public void Stop ()
		{
			check_inferior ();
			inferior.Stop ();
		}

		public void ClearSignal ()
		{
			check_stopped ();
			inferior.SetSignal (0, false);
		}

		public void Finish ()
		{
			check_can_run ();
			sse.Finish ();
		}

		public void Kill ()
		{
			check_disposed ();
			if (inferior != null)
				inferior.Shutdown ();
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_stopped ();
				return inferior.CurrentFrame;
			}
		}

		public StackFrame CurrentFrame {
			get {
				check_stopped ();
				if (sse != null)
					return sse.CurrentFrame;
				else
					return core.CurrentFrame;
			}
		}

		public StackFrame[] GetBacktrace ()
		{
			check_stopped ();
			if (sse != null)
				return sse.GetBacktrace ();
			else
				return core.GetBacktrace ();
		}

		public long GetRegister (int register)
		{
			check_stopped ();
			return inferior.GetRegister (register);
		}

		public long[] GetRegisters (int[] registers)
		{
			check_stopped ();
			return inferior.GetRegisters (registers);
		}

		public void SetRegister (int register, long value)
		{
			check_stopped ();
			inferior.SetRegister (register, value);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			check_stopped ();
			inferior.SetRegisters (registers, values);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_inferior ();
			return inferior.GetMemoryMaps ();
		}

		public Process CreateThread (int pid)
		{
			Process new_process = new Process (backend, start, bfd_container);
			new_process.SingleSteppingEngine.Attach (pid);
			return new_process;
		}

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;

			sse = null;
		}

		void check_inferior ()
		{
			check_disposed ();
			if (inferior == null)
				throw new NoTargetException ();
		}

		// <remarks>
		//   If you don't want to get an exception, check CanStep prior to
		//   issuing the stepping command.
		// </remarks>
		void check_can_run ()
		{
			check_inferior ();

			if (sse == null)
				throw new CannotExecuteCoreFileException ();
			else if (sse.State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();
		}

		void check_stopped ()
		{
			check_inferior ();
			if (!IsStopped)
				throw new TargetNotStoppedException ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
					if (inferior != null)
						inferior.Kill ();
				}

				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					// Nothing to do yet.
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Process ()
		{
			Dispose (false);
		}
	}
}
