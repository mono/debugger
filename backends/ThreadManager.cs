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
	public delegate void ThreadEventHandler (ThreadManager manager, Process process);

	public abstract class ThreadManager : IDisposable
	{
		protected readonly BfdContainer bfdc;
		protected readonly DebuggerBackend backend;
		protected readonly BreakpointManager breakpoint_manager;

		protected Process main_process;

		bool initialized = false;

		protected ThreadManager (DebuggerBackend backend, BfdContainer bfdc)
		{
			this.backend = backend;
			this.bfdc = bfdc;

			breakpoint_manager = new BreakpointManager ();
		}

		public abstract Process StartApplication (ProcessStart start);

		internal abstract Inferior CreateInferior (ProcessStart start);

		protected abstract void DoInitialize (Inferior inferior, bool activate);

		protected void Initialize (Inferior inferior, bool activate)
		{
			if (!initialized) {
				DoInitialize (inferior, activate);
				initialized = true;
			}
		}

		protected Process CreateThread (Inferior inferior, int pid)
		{
			Inferior new_inferior = inferior.CreateThread ();
			Process new_process = new NativeThread (new_inferior, pid);

			AddThread (inferior, new_process, pid, false);

			return new_process;
		}

		protected Process CreateThread (Inferior inferior, int pid,
						DaemonThreadHandler handler)
		{
			Inferior new_inferior = inferior.CreateThread ();
			Process new_process = new DaemonThread (new_inferior, pid, handler);

			AddThread (inferior, new_process, pid, true);

			return new_process;
		}

		protected Process CreateMainThread (Inferior inferior)
		{
			return new NativeProcess (this, inferior);
		}

		protected abstract void AddThread (Inferior inferior, Process new_process,
						   int pid, bool is_daemon);

		public bool Initialized {
			get { return initialized; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		internal BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		public Process MainProcess {
			get { return main_process; }
		}

		public abstract AddressDomain AddressDomain {
			get;
		}

		public event ThreadEventHandler InitializedEvent;
		public event ThreadEventHandler MainThreadCreatedEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event TargetExitedHandler TargetExitedEvent;

		public event TargetOutputHandler TargetOutputEvent;
		public event TargetOutputHandler TargetErrorOutputEvent;
		public event DebuggerOutputHandler DebuggerOutputEvent;
		public event DebuggerErrorHandler DebuggerErrorEvent;

		protected virtual void OnInitializedEvent (Process new_process)
		{
			if (InitializedEvent != null)
				InitializedEvent (this, new_process);
		}

		protected virtual void OnMainThreadCreatedEvent (Process new_process)
		{
			if (MainThreadCreatedEvent != null)
				MainThreadCreatedEvent (this, new_process);
		}

		protected virtual void OnThreadCreatedEvent (Process new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		protected virtual void OnThreadExitedEvent (Process process)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, process);
		}

		protected virtual void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		public abstract Process[] Threads {
			get;
		}

		public void Kill ()
		{
			if (main_process != null)
				main_process.Kill ();
		}

		protected enum ThreadManagerCommand {
			Unknown,
			CreateThread,
			ResumeThread,
			AcquireGlobalLock,
			ReleaseGlobalLock
		};

		protected class ThreadData {
			public readonly int TID;
			public readonly int PID;
			public readonly bool IsManaged;
			public readonly Process Process;
			public readonly TargetAddress StartStack;
			public readonly TargetAddress Data;

			public ThreadData (Process process, int tid, int pid,
					   TargetAddress start_stack, TargetAddress data)
			{
				this.IsManaged = true;
				this.Process = process;
				this.TID = tid;
				this.PID = pid;
				this.StartStack = start_stack;
				this.Data = data;
			}

			public ThreadData (Process process, int pid)
			{
				this.IsManaged = false;
				this.Process = process;
				this.TID = -1;
				this.PID = pid;
				this.StartStack = TargetAddress.Null;
				this.Data = TargetAddress.Null;
			}
		};

		void inferior_output (bool is_stderr, string line)
		{
			if (TargetOutputEvent != null)
				TargetOutputEvent (is_stderr, line);
		}

		void debugger_output (string line)
		{
			if (DebuggerOutputEvent != null)
				DebuggerOutputEvent (line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			if (DebuggerErrorEvent != null)
				DebuggerErrorEvent (this, message, e);
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		internal abstract void AcquireGlobalThreadLock (Inferior inferior, Process caller);
		internal abstract void ReleaseGlobalThreadLock (Inferior inferior, Process caller);

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
		}

		protected abstract void DoDispose ();

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
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

		~ThreadManager ()
		{
			Dispose (false);
		}

		protected class NativeProcess : Process
		{
			ThreadManager thread_manager;
			protected readonly Inferior inferior;

			public NativeProcess (ThreadManager thread_manager, Inferior inferior)
				: base (inferior)
			{
				this.thread_manager = thread_manager;
				this.inferior = inferior;
			}

			internal override void RunInferior ()
			{
				inferior.Run (false);

				TargetAddress main = inferior.MainMethodAddress;
				Report.Debug (DebugFlags.Threads, "Main address is {0}", main);
				inferior.Continue (main);

				inferior.UpdateModules ();

				thread_manager.Initialize (inferior, false);
			}
		}

		protected class NativeThread : Process
		{
			protected readonly Inferior inferior;
			protected readonly int tid;

			public NativeThread (Inferior inferior, int tid)
				: base (inferior)
			{
				this.inferior = inferior;
				this.tid = tid;
			}

			internal override void RunInferior ()
			{
				inferior.Attach (tid);
			}
		}

		protected class DaemonThread : Process
		{
			protected readonly Inferior inferior;
			protected readonly DaemonThreadRunner runner;
			protected readonly DaemonThreadHandler handler;
			protected readonly int tid;

			public DaemonThread (Inferior inferior, int tid,
					     DaemonThreadHandler handler)
				: base (inferior)
			{
				this.inferior = inferior;
				this.handler = handler;
				this.tid = tid;
				
				runner = new DaemonThreadRunner (this, inferior, tid, sse, handler);
				SetDaemonThreadRunner (runner);
			}

			internal override void RunInferior ()
			{
				inferior.Attach (tid);
			}
		}
	}
}
