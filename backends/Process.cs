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
	public delegate void ProcessExitedHandler (Process process);

	public class Process : IProcess, ITargetNotification, IDisposable
	{
		DebuggerBackend backend;
		ProcessStart start;
		BfdContainer bfd_container;

		SingleSteppingEngine sse;
		DaemonThreadRunner runner;
		IProcess iprocess;
		bool is_daemon;
		int pid, id;

		static int next_id = 0;
		static int next_daemon_id = 0;

		protected enum ProcessType
		{
			Normal,
			CoreFile,
			Daemon,
			ManagedWrapper,
			CommandProcess
		}

		protected Process (DebuggerBackend backend, ProcessStart start, BfdContainer bfd_container,
				   ProcessType type, string core_file, int pid, DaemonThreadHandler handler,
				   int signal)
		{
			this.backend = backend;
			this.start = start;
			this.bfd_container = bfd_container;

			IInferior inferior = new PTraceInferior (
				backend, start, bfd_container, backend.ThreadManager.BreakpointManager,
				new DebuggerErrorHandler (debugger_error));

			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.DebuggerOutput += new TargetOutputHandler (debugger_output);
			inferior.DebuggerError += new DebuggerErrorHandler (debugger_error);

			if ((type == ProcessType.Normal) && (pid == -1) && !start.IsNative)
				type = ProcessType.ManagedWrapper;

			switch (type) {
			case ProcessType.Daemon:
				is_daemon = true;
				id = --next_daemon_id;
				runner = new DaemonThreadRunner (backend, this, inferior, handler, pid, signal);
				runner.TargetExited += new TargetExitedHandler (child_exited);
				this.pid = pid;
				break;

			case ProcessType.ManagedWrapper:
				is_daemon = true;
				id = --next_daemon_id;
				runner = backend.ThreadManager.StartManagedApplication (this, inferior, start);
				runner.TargetExited += new TargetExitedHandler (child_exited);
				this.pid = runner.Inferior.PID;
				break;

			case ProcessType.CommandProcess:
				is_daemon = true;
				goto case ProcessType.Normal;

			case ProcessType.Normal:
				if (is_daemon)
					id = --next_daemon_id;
				else
					id = ++next_id;
				sse = new SingleSteppingEngine (backend, this, inferior, start.IsNative);

				sse.StateChangedEvent += new StateChangedHandler (target_state_changed);
				sse.MethodInvalidEvent += new MethodInvalidHandler (method_invalid);
				sse.MethodChangedEvent += new MethodChangedHandler (method_changed);
				sse.FrameChangedEvent += new StackFrameHandler (frame_changed);
				sse.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);

				if (pid != -1) {
					this.pid = pid;
					sse.Attach (pid, true);
				}

				iprocess = sse;
				break;

			case ProcessType.CoreFile:
				id = ++next_id;
				CoreFile core = new CoreFileElfI386 (backend, this, start.TargetApplication,
								     core_file, bfd_container);

				backend.InitializeCoreFile (this, core);
				iprocess = core;
				break;

			default:
				throw new InternalError ();
			}
		}

		internal Process (DebuggerBackend backend, ProcessStart start, BfdContainer bfd_container,
				  string core_file)
			: this (backend, start, bfd_container, ProcessType.CoreFile, core_file, -1, null, 0)
		{ }

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

		public ITargetMemoryInfo TargetMemoryInfo {
			get {
				check_iprocess ();
				return iprocess.TargetMemoryInfo;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_iprocess ();
				return iprocess.TargetMemoryAccess;
			}
		}

		public ITargetAccess TargetAccess {
			get {
				check_iprocess ();
				return iprocess.TargetAccess;
			}
		}

		public IDisassembler Disassembler {
			get {
				check_iprocess ();
				return iprocess.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_iprocess ();
				return iprocess.Architecture;
			}
		}

		//
		// ITargetNotification
		//

		public int ID {
			get {
				return id;
			}
		}

		public int PID {
			get{
				return pid;
			}
		}

		public TargetState State {
			get {
				if (is_daemon)
					return TargetState.DAEMON;
				else if (iprocess != null)
					return iprocess.State;
				else
					return TargetState.NO_TARGET;
			}
		}

		void target_state_changed (TargetState new_state, int arg)
		{
			if ((pid == 0) && (sse != null))
				pid = sse.PID;
			if (StateChanged != null)
				StateChanged (new_state, arg);
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event TargetOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;
		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;
		public event ProcessExitedHandler ProcessExitedEvent;

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
			get { return iprocess != null; }
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

		public bool StepInstruction (bool synchronous)
		{
			check_sse ();
			return sse.StepInstruction (synchronous);
		}

		public bool NextInstruction (bool synchronous)
		{
			check_sse ();
			return sse.NextInstruction (synchronous);
		}

		public bool StepLine (bool synchronous)
		{
			check_sse ();
			return sse.StepLine (synchronous);
		}

		public bool NextLine (bool synchronous)
		{
			check_sse ();
			return sse.NextLine (synchronous);
		}

		public bool Continue (bool synchronous)
		{
			check_sse ();
			return sse.Continue (false, synchronous);
		}

		public bool Continue (bool in_background, bool synchronous)
		{
			check_sse ();
			return sse.Continue (in_background, synchronous);
		}

		public bool Continue (TargetAddress until, bool synchronous)
		{
			check_sse ();
			return sse.Continue (until, synchronous);
		}

		public void Stop ()
		{
			check_disposed ();
			if (sse != null)
				sse.Stop ();
		}

		public void ClearSignal ()
		{
			SetSignal (0, false);
		}

		public void SetSignal (int signal, bool send_it)
		{
			check_sse ();
			sse.SetSignal (signal, send_it);
		}

		public bool Finish (bool synchronous)
		{
			check_sse ();
			return sse.Finish (synchronous);
		}

		public void Kill ()
		{
			if (disposed)
				return;
			if (sse != null)
				sse.Kill ();
			child_exited ();
			Dispose ();
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_iprocess ();
				return iprocess.CurrentFrameAddress;
			}
		}

		public StackFrame CurrentFrame {
			get {
				check_iprocess ();
				return iprocess.CurrentFrame;
			}
		}

		public Backtrace GetBacktrace ()
		{
			check_iprocess ();
			return iprocess.GetBacktrace ();
		}

		public Register[] GetRegisters ()
		{
			check_iprocess ();
			return iprocess.GetRegisters ();
		}

		public virtual long GetRegister (int index)
		{
			check_iprocess ();
			return iprocess.TargetAccess.GetRegister (index);
		}

		public virtual long[] GetRegisters (int[] indices)
		{
			long[] retval = new long [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetRegister (indices [i]);
			return retval;
		}

		public void SetRegister (int register, long value)
		{
			check_sse ();
			sse.SetRegister (register, value);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			check_sse ();
			sse.SetRegisters (registers, values);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_iprocess ();
			return iprocess.GetMemoryMaps ();
		}

		internal static Process StartApplication (DebuggerBackend backend, ProcessStart start,
							  BfdContainer bfd_container)
		{
			Process process = new Process (
				backend, start, bfd_container, ProcessType.Normal, null, -1, null, 0);
			if (backend.ThreadManager.MainProcess != null)
				return backend.ThreadManager.MainProcess;
			else
				return process;
		}

		public Process CreateThread (int pid)
		{
			return new Process (backend, start, bfd_container, ProcessType.Normal,
					    null, pid, null, 0);
		}

		public Process CreateDaemonThread (int pid)
		{
			return new Process (backend, start, bfd_container, ProcessType.CommandProcess,
					    null, pid, null, 0);
		}

		public Process CreateDaemonThread (int pid, int signal, DaemonThreadHandler handler)
		{
			return new Process (backend, start, bfd_container, ProcessType.Daemon,
					    null, pid, handler, signal);
		}

		public ProcessStart ProcessStart {
			get { return start; }
		}

		void child_exited ()
		{
			if (TargetExited != null)
				TargetExited ();
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
		}

		void check_iprocess ()
		{
			check_disposed ();
			if (iprocess == null)
				throw new NoTargetException ();
		}

		void check_sse ()
		{
			check_disposed ();
			if (sse == null)
				throw new NoTargetException ();
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
					if (iprocess != null)
						iprocess.Dispose ();
					if (runner != null)
						runner.Dispose ();
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
