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

	public class ThreadManager : IDisposable
	{
		BfdContainer bfdc;
		DebuggerBackend backend;
		Hashtable thread_hash;
		Process main_process;
		Process debugger_process;
		Process manager_process;
		Mutex thread_lock_mutex;
		AddressDomain address_domain;
		BreakpointManager breakpoint_manager;
		DaemonThreadHandler csharp_handler;
		AutoResetEvent thread_started_event;
		ThreadGroup global_group;
		int thread_lock_level = 0;

		ManualResetEvent main_started_event;

		TargetAddress thread_handles = TargetAddress.Null;
		TargetAddress thread_handles_num = TargetAddress.Null;
		TargetAddress last_thread_event = TargetAddress.Null;
		bool initialized = false;
		bool reached_main = false;

		TargetAddress mono_thread_notification = TargetAddress.Null;
		TargetAddress thread_manager_last_pid = TargetAddress.Null;
		TargetAddress thread_manager_last_func = TargetAddress.Null;
		TargetAddress thread_manager_last_thread = TargetAddress.Null;
		TargetAddress thread_manager_background_pid = TargetAddress.Null;

		internal ThreadManager (DebuggerBackend backend, BfdContainer bfdc)
		{
			this.backend = backend;
			this.bfdc = bfdc;
			this.thread_hash = Hashtable.Synchronized (new Hashtable ());

			global_group = ThreadGroup.CreateThreadGroup ("global");

			thread_lock_mutex = new Mutex ();
			breakpoint_manager = new BreakpointManager ();

			thread_started_event = new AutoResetEvent (false);
			main_started_event = new ManualResetEvent (false);

			address_domain = new AddressDomain ("global");
		}

		public bool Initialize (Process process, IInferior inferior)
		{
			this.csharp_handler = csharp_handler;
			this.main_process = process;
			add_process (process, process.PID, false);

			TargetAddress tdebug = bfdc.LookupSymbol ("__pthread_threads_debug");

			thread_handles = bfdc.LookupSymbol ("__pthread_handles");
			thread_handles_num = bfdc.LookupSymbol ("__pthread_handles_num");
			last_thread_event = bfdc.LookupSymbol ("__pthread_last_event");

			if (tdebug.IsNull || thread_handles.IsNull ||
			    thread_handles_num.IsNull || last_thread_event.IsNull) {
				OnInitializedEvent (main_process);
				OnMainThreadCreatedEvent (main_process);
				return false;
			}

			inferior.WriteInteger (tdebug, 1);
			initialized = true;

			OnInitializedEvent (main_process);
			OnMainThreadCreatedEvent (main_process);

			return true;
		}

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

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		public event ThreadEventHandler InitializedEvent;
		public event ThreadEventHandler MainThreadCreatedEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;

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

		public Process[] Threads {
			get {
				lock (this) {
					Process[] procs = new Process [thread_hash.Values.Count];
					thread_hash.Values.CopyTo (procs, 0);
					return procs;
				}
			}
		}

		public bool SignalHandler (IInferior inferior, int signal, out bool action)
		{
			if (signal == PTraceInferior.ThreadRestartSignal) {
				action = false;
				return true;
			}

			if (signal == PTraceInferior.ThreadAbortSignal) {
				action = false;
				return true;
			}

			if ((signal == PTraceInferior.SIGCHLD) || (signal == PTraceInferior.SIGPROF)) {
				inferior.SetSignal (0, false);
				action = false;
				return true;
			}

			if (signal == PTraceInferior.SIGINT) {
				inferior.SetSignal (0, false);
				action = true;
				return true;
			}

			if (signal != PTraceInferior.ThreadDebugSignal) {
				action = true;
				return false;
			}

			reload_threads (inferior);

			inferior.SetSignal (PTraceInferior.ThreadRestartSignal, false);
			action = false;
			return true;
		}

		void process_exited (Process process)
		{
			OnThreadExitedEvent (process);

			lock (this) {
				thread_hash.Remove (process.PID);
				if (process != main_process)
					return;

				foreach (Process thread in thread_hash.Values)
					thread.Dispose ();
			}
		}

		void add_process (Process process, int pid, bool send_event)
		{
			thread_hash.Add (pid, process);
			if (process.State != TargetState.DAEMON)
				global_group.AddThread (process);
			process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
			if (send_event)
				OnThreadCreatedEvent (process);
		}

		void reload_threads (ITargetMemoryAccess memory)
		{
			int size = memory.TargetIntegerSize * 2 + memory.TargetAddressSize * 2;
			int offset = memory.TargetIntegerSize * 2;

			int count = memory.ReadInteger (thread_handles_num);
			for (int index = 0; index <= count; index++) {
				TargetAddress thandle_addr = thread_handles + index * size + offset;

				TargetAddress thandle = memory.ReadAddress (thandle_addr);

				if (thandle.IsNull || (thandle.Address == 0))
					continue;

				thandle += 20 * memory.TargetAddressSize;
				int tid = memory.ReadInteger (thandle);
				thandle += memory.TargetIntegerSize;
				int pid = memory.ReadInteger (thandle);

				if (thread_hash.Contains (pid))
					continue;

				Process new_process;
				if (index == 1)
					new_process = main_process.CreateDaemonThread (
						pid, PTraceInferior.ThreadRestartSignal, null);
				else if ((csharp_handler != null) && (index == 2))
					new_process = debugger_process = main_process.CreateDaemonThread (
						pid, PTraceInferior.ThreadRestartSignal, csharp_handler);
				else {
					new_process = main_process.CreateThread (pid);
					new_process.SetSignal (PTraceInferior.ThreadRestartSignal, true);
				}

				add_process (new_process, pid, true);
			}
		}

		int last_thread = 0;
		SingleSteppingEngine last_sse = null;

		void thread_started (StackFrame frame, int index, object user_data)
		{
			Console.WriteLine ("THREAD STARTED: {0} {1}", frame, index);
			thread_started_event.Set ();
		}

		bool mono_thread_manager (DaemonThreadRunner runner, TargetAddress address, int signal)
		{
			if (!initialized) {
				TargetAddress maddr = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notification");
				TargetAddress dpid = bfdc.LookupSymbol ("MONO_DEBUGGER__debugger_thread");
				TargetAddress mpid = bfdc.LookupSymbol ("MONO_DEBUGGER__main_thread");

				if (maddr.IsNull || dpid.IsNull || mpid.IsNull)
					return false;

				thread_manager_last_pid = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_last_pid");
				thread_manager_last_func = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_last_func");
				thread_manager_last_thread = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_last_thread");

				mono_thread_notification = runner.Inferior.ReadGlobalAddress (maddr);
				if (address != mono_thread_notification)
					return false;

				int debugger_pid = runner.Inferior.ReadInteger (dpid);
				int main_pid = runner.Inferior.ReadInteger (mpid);

				manager_process = runner.Process;
				add_process (manager_process, manager_process.PID, true);

				debugger_process = backend.CreateDebuggerProcess (runner.Process, debugger_pid);
				add_process (debugger_process, debugger_pid, true);

				main_process = runner.Process.CreateThread (main_pid);
				add_process (main_process, main_pid, false);

				initialized = true;
				OnInitializedEvent (main_process);

				return true;
			}

			//
			// Already initialized.
			//

			if ((address != mono_thread_notification) || (signal != 0))
				return false;

			if (!reached_main) {
				TargetAddress mfunc = bfdc.LookupSymbol ("MONO_DEBUGGER__main_function");
				TargetAddress main_function = runner.Inferior.ReadGlobalAddress (mfunc);

				backend.ReachedManagedMain (main_process);
				main_process.SingleSteppingEngine.Continue (main_function, true);
				OnMainThreadCreatedEvent (main_process);
				main_process.SingleSteppingEngine.ReachedMain ();

				reached_main = true;
				main_started_event.Set ();
				return true;
			}

			int pid = runner.Inferior.ReadInteger (thread_manager_last_pid);
			TargetAddress func = runner.Inferior.ReadAddress (thread_manager_last_func);
			int thread = runner.Inferior.ReadInteger (thread_manager_last_thread);

			if (pid == -1) {
				if (thread != last_thread)
					throw new InternalError ();

				thread_started_event.WaitOne ();
				return true;
			}

			Process new_process = runner.Process.CreateThread (pid);
			new_process.SetSignal (PTraceInferior.ThreadRestartSignal, true);

			add_process (new_process, pid, true);

			if (func.Address == 0)
				throw new InternalError ("Created thread without start function");

			last_thread = thread;
			last_sse = new_process.SingleSteppingEngine;
			int ret = last_sse.InsertBreakpoint (
				func, null, new BreakpointHitHandler (thread_started), true, null);

			new_process.SingleSteppingEngine.Continue (true, false);

			return true;
		}

		internal DaemonThreadRunner StartManagedApplication (Process process, IInferior inferior,
								     ProcessStart start)
		{
			DaemonThreadHandler handler = new DaemonThreadHandler (mono_thread_manager);
			DaemonThreadRunner runner = new DaemonThreadRunner (
				backend, process, inferior, handler, start);

			main_started_event.WaitOne ();
			return runner;
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		public void AcquireGlobalThreadLock (Process caller)
		{
			thread_lock_mutex.WaitOne ();
			if (thread_lock_level++ > 0)
				return;
			foreach (Process process in thread_hash.Values) {
				if ((process == caller) || (process.SingleSteppingEngine == null))
					continue;
				process.SingleSteppingEngine.AcquireThreadLock ();
			}
		}

		public void ReleaseGlobalThreadLock (Process caller)
		{
			if (--thread_lock_level > 0) {
				thread_lock_mutex.ReleaseMutex ();
				return;
			}
				
			foreach (Process process in thread_hash.Values) {
				if ((process == caller) || (process.SingleSteppingEngine == null))
					continue;
				process.SingleSteppingEngine.ReleaseThreadLock ();
			}
			thread_lock_mutex.ReleaseMutex ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					foreach (Process thread in thread_hash.Values)
						thread.Dispose ();
					breakpoint_manager.Dispose ();
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

		~ThreadManager ()
		{
			Dispose (false);
		}
	}
}
