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

namespace Mono.Debugger
{
	public delegate bool BreakpointCheckHandler (StackFrame frame, int index, object user_data);
	public delegate void BreakpointHitHandler (StackFrame frame, int index, object user_data);
	public delegate void ProcessExitedHandler (Process process);

	public abstract class Process : IDisposable
	{
		ProcessStart start;

		int id;
		protected bool is_daemon;
		static int next_id = 0;

		protected Process (ProcessStart start)
		{
			this.start = start;

			id = ++next_id;
		}

		public abstract ITargetMemoryInfo TargetMemoryInfo {
			get;
		}

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract ITargetAccess TargetAccess {
			get;
		}

		public abstract IDisassembler Disassembler {
			get;
		}

		public abstract IArchitecture Architecture {
			get;
		}

		//
		// ITargetNotification
		//

		public int ID {
			get {
				return id;
			}
		}

		public abstract int PID {
			get;
		}

		public abstract int TID {
			get;
		}

		public abstract TargetState State {
			get;
		}

		public bool IsDaemon {
			get {
				return is_daemon;
			}
		}

		protected virtual void OnTargetEvent (TargetEventArgs args)
		{
			if (TargetEvent != null)
				TargetEvent (this, args);
		}

		protected virtual void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		public event TargetOutputHandler TargetOutput;
		public event DebuggerOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;

		// <summary>
		//   This event is emitted each time a stepping operation is started or
		//   completed.  Other than the Inferior's StateChangedEvent, it is only
		//   emitted after the whole operation completed.
		// </summary>
		public event TargetEventHandler TargetEvent;
		public event TargetExitedHandler TargetExitedEvent;
		public event ProcessExitedHandler ProcessExitedEvent;

		protected virtual void OnInferiorOutput (bool is_stderr, string line)
		{
			if (TargetOutput != null)
				TargetOutput (is_stderr, line);
		}

		protected virtual void OnDebuggerOutput (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		protected virtual void OnDebuggerError (object sender, string message,
							Exception e)
		{
			if (DebuggerError != null)
				DebuggerError (this, message, e);
		}

		// <summary>
		//   If true, we have a target.
		// </summary>
		public abstract bool HasTarget {
			get;
		}

		// <summary>
		//   If true, we have a target which can be executed (ie. it's not a core file).
		// </summary>
		public abstract bool CanRun {
			get;
		}

		// <summary>
		//   If true, we have a target which can be executed and it is currently stopped
		//   so that we can issue a step command.
		// </summary>
		public abstract bool CanStep {
			get;
		}

		// <summary>
		//   If true, the target is currently stopped and thus its memory/registers can
		//   be read/writtern.
		// </summary>
		public abstract bool IsStopped {
			get;
		}

		public abstract bool StepInstruction (bool synchronous);

		public abstract bool StepNativeInstruction (bool synchronous);

		public abstract bool NextInstruction (bool synchronous);

		public abstract bool StepLine (bool synchronous);

		public abstract bool NextLine (bool synchronous);

		public bool Continue (TargetAddress until, bool synchronous)
		{
			return Continue (until, false, synchronous);
		}

		public bool Continue (bool in_background, bool synchronous)
		{
			return Continue (TargetAddress.Null, in_background, synchronous);
		}

		public bool Continue (bool synchronous)
		{
			return Continue (TargetAddress.Null, false, synchronous);
		}

		public abstract bool Continue (TargetAddress until, bool in_background,
					       bool synchronous);

		public abstract void Stop ();

		public abstract bool Finish (bool synchronous);

		public abstract void Kill ();

		public abstract TargetAddress CurrentFrameAddress {
			get;
		}

		public abstract StackFrame CurrentFrame {
			get;
		}

		public abstract Backtrace GetBacktrace ();

		public abstract Backtrace GetBacktrace (int max_frames);

		public abstract Register[] GetRegisters ();

		public abstract long GetRegister (int index);

		public virtual long[] GetRegisters (int[] indices)
		{
			long[] retval = new long [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetRegister (indices [i]);
			return retval;
		}

		public abstract void SetRegister (int register, long value);

		public abstract void SetRegisters (int[] registers, long[] values);

		public abstract TargetMemoryArea[] GetMemoryMaps ();

		public ProcessStart ProcessStart {
			get { return start; }
		}

		public abstract int InsertBreakpoint (BreakpointHandle handle,
						      TargetAddress address,
						      BreakpointCheckHandler check_handler,
						      BreakpointHitHandler hit_handler,
						      bool needs_frame, object user_data);

		public abstract void RemoveBreakpoint (int index);

		void child_exited ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
			Dispose ();
		}

		public abstract Register[] AcquireThreadLock ();
		public abstract void ReleaseThreadLock ();

		public abstract long CallMethod (TargetAddress method, long method_argument,
						 string string_argument);

		public override string ToString ()
		{
			return String.Format ("Process ({0}:{1}:{2}:{3:x})",
					      IsDaemon, id, PID, TID);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected abstract void DoDispose ();

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				DoDispose ();
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
