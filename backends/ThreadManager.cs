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
	public delegate void ThreadEventHandler (ThreadManager manager, Process process);

	public class ThreadManager : IDisposable
	{
		BfdContainer bfdc;
		DebuggerBackend backend;
		Hashtable thread_hash;
		Process main_process;
		Process debugger_process;
		Mutex thread_lock_mutex;
		BreakpointManager breakpoint_manager;
		DaemonThreadHandler csharp_handler;
		int thread_lock_level = 0;

		TargetAddress thread_handles = TargetAddress.Null;
		TargetAddress thread_handles_num = TargetAddress.Null;
		TargetAddress last_thread_event = TargetAddress.Null;
		bool initialized = false;

		const int Signal_SIGINT			= 2;
		const int Signal_SIGCHLD		= 17;

		const int PThread_Signal_Debug		= 34;
		const int PThread_Signal_Abort		= 33;
		const int PThread_Signal_Restart	= 32;

		internal ThreadManager (DebuggerBackend backend, BfdContainer bfdc)
		{
			this.backend = backend;
			this.bfdc = bfdc;
			this.thread_hash = new Hashtable ();

			thread_lock_mutex = new Mutex ();
			breakpoint_manager = new BreakpointManager ();
		}

		public bool Initialize (Process process, DaemonThreadHandler csharp_handler)
		{
			this.csharp_handler = csharp_handler;
			this.main_process = process;
			thread_hash.Add (process.Inferior.PID, process);

			TargetAddress tdebug = bfdc.LookupSymbol ("__pthread_threads_debug");

			thread_handles = bfdc.LookupSymbol ("__pthread_handles");
			thread_handles_num = bfdc.LookupSymbol ("__pthread_handles_num");
			last_thread_event = bfdc.LookupSymbol ("__pthread_last_event");

			if (tdebug.IsNull || thread_handles.IsNull ||
			    thread_handles_num.IsNull || last_thread_event.IsNull)
				return false;

			process.Inferior.WriteInteger (tdebug, 1);
			initialized = true;

			reload_threads (process.Inferior);

			Console.WriteLine ("Initialized thread manager.");

			OnInitializedEvent (main_process);

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

		public event ThreadEventHandler InitializedEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;

		protected virtual void OnInitializedEvent (Process new_process)
		{
			if (InitializedEvent != null)
				InitializedEvent (this, new_process);
		}

		protected virtual void OnThreadCreatedEvent (Process new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		public Process[] Threads {
			get {
				Process[] procs = new Process [thread_hash.Values.Count];
				thread_hash.Values.CopyTo (procs, 0);
				return procs;
			}
		}

		public bool SignalHandler (Process process, int signal, out bool action)
		{
			if (signal == PThread_Signal_Restart) {
				action = false;
				return true;
			}

			if (signal == PThread_Signal_Abort) {
				action = false;
				return true;
			}

			if (signal == Signal_SIGCHLD) {
				process.Inferior.SetSignal (0, false);
				action = false;
				return true;
			}

			if (signal == Signal_SIGINT) {
				process.Inferior.SetSignal (0, false);
				action = true;
				return true;
			}

			if (signal != PThread_Signal_Debug) {
				action = true;
				return false;
			}

			reload_threads (process.Inferior);

			process.Inferior.SetSignal (PThread_Signal_Restart, false);
			action = false;
			return true;
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
						pid, PThread_Signal_Restart, null);
				else if ((csharp_handler != null) && (index == 2))
					new_process = debugger_process = main_process.CreateDaemonThread (
						pid, 0, csharp_handler);
				else {
					new_process = main_process.CreateThread (pid);
					new_process.Inferior.SetSignal (PThread_Signal_Restart, true);
				}

				thread_hash.Add (pid, new_process);

				OnThreadCreatedEvent (new_process);
			}
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
				if (process == caller)
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
				if (process == caller)
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
