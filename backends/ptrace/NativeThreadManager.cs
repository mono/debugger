using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Architecture;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Backends
{
#if FIXME
	internal class NativeThreadManager : ThreadManager
	{
		public NativeThreadManager (DebuggerBackend backend, BfdContainer bfdc,
					    bool activate)
			: base (backend, bfdc)
		{
			this.activate = activate;

			thread_hash = Hashtable.Synchronized (new Hashtable ());

			global_group = ThreadGroup.CreateThreadGroup ("global");
			thread_lock_mutex = new Mutex ();
			address_domain = new AddressDomain ("global");
		}

		public override AddressDomain AddressDomain {
			get { return address_domain; }
		}

		Hashtable thread_hash;
		Mutex thread_lock_mutex;
		AddressDomain address_domain;
		ThreadGroup global_group;
		int thread_lock_level = 0;
		bool activate;

		TargetAddress thread_handles = TargetAddress.Null;
		TargetAddress thread_handles_num = TargetAddress.Null;
		TargetAddress last_thread_event = TargetAddress.Null;

		bool has_threads;
		int debug_signal, restart_signal, cancel_signal;

		internal override Inferior CreateInferior (ProcessStart start)
		{
			return new PTraceInferior (
				backend, start, bfdc, breakpoint_manager, null,
				address_domain, this);
		}

#if FIXME
		public override Process StartApplication (ProcessStart start)
		{
			Inferior inferior = CreateInferior (start);

			Report.Debug (DebugFlags.Threads, "Starting application: {0}", start);

			main_process = new NativeProcess (this, inferior);
			main_process.Run ();

			Report.Debug (DebugFlags.Threads, "Done starting application");

			OnInitializedEvent (main_process);
			OnMainThreadCreatedEvent (main_process);

			Report.Debug (DebugFlags.Threads, "Done sending events");

			return main_process;
		}
#endif

		protected override void DoInitialize (Inferior inferior)
		{
			TargetAddress tdebug = bfdc.LookupSymbol ("__pthread_threads_debug");

			thread_handles = bfdc.LookupSymbol ("__pthread_handles");
			thread_handles_num = bfdc.LookupSymbol ("__pthread_handles_num");
			last_thread_event = bfdc.LookupSymbol ("__pthread_last_event");

			TargetAddress sig_debug = bfdc.LookupSymbol ("__pthread_sig_debug");
			TargetAddress sig_restart = bfdc.LookupSymbol ("__pthread_sig_restart");
			TargetAddress sig_cancel = bfdc.LookupSymbol ("__pthread_sig_cancel");

			if (!tdebug.IsNull && !thread_handles.IsNull &&
			    !thread_handles_num.IsNull && !last_thread_event.IsNull &&
			    !sig_debug.IsNull && !sig_restart.IsNull && !sig_cancel.IsNull) {
				if (activate) {
					add_process (main_process, inferior.PID);
					inferior.WriteInteger (tdebug, 1);
					has_threads = true;
				}

				debug_signal = inferior.ReadInteger (sig_debug);
				restart_signal = inferior.ReadInteger (sig_restart);
				cancel_signal = inferior.ReadInteger (sig_cancel);
			}
		}

		protected virtual void ReloadThreads (Inferior inferior)
		{
			Report.Debug (DebugFlags.Threads, "Reloading threads");

			int size = inferior.TargetIntegerSize * 2 + inferior.TargetAddressSize * 2;
			int offset = inferior.TargetIntegerSize * 2;

			int count = inferior.ReadInteger (thread_handles_num);
			for (int index = 0; index <= count; index++) {
				TargetAddress thandle_addr = thread_handles + index * size + offset;

				TargetAddress thandle = inferior.ReadAddress (thandle_addr);

				if (thandle.IsNull || (thandle.Address == 0))
					continue;

				thandle += 20 * inferior.TargetAddressSize;
				int tid = inferior.ReadInteger (thandle);
				thandle += inferior.TargetIntegerSize;
				int pid = inferior.ReadInteger (thandle);

				if (thread_hash.Contains (pid))
					continue;

				Process new_thread;
				if (index == 1)
					new_thread = CreateThread (inferior, pid, null);
				else
					new_thread = CreateThread (inferior, pid);

				OnThreadCreatedEvent (new_thread);
			}
		}

		protected override void AddThread (Inferior inferior, Process new_process,
						   int pid, bool is_daemon)
		{
			add_process (new_process, pid);
			new_process.Run ();
			if (!is_daemon)
				((PTraceInferior) inferior).SetSignal (restart_signal, true);

			Report.Debug (DebugFlags.Threads, "New thread: {0}", new_process);
		}

		bool main_exited = false;

		void process_exited (Process process)
		{
			OnThreadExitedEvent (process);

			if (main_exited)
				return;

			lock (this) {
				thread_hash.Remove (process.PID);
				if (process != main_process)
					return;

				main_exited = true;

				foreach (ThreadData data in thread_hash.Values)
					data.Process.Kill ();

				OnTargetExitedEvent ();
			}
		}

		void add_process (ThreadData data, int pid)
		{
			thread_hash.Add (pid, data);
			if (data.Process.State != TargetState.DAEMON)
				global_group.AddThread (data.Process);
			data.Process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
		}

		void add_process (Process process, int pid)
		{
			add_process (new ThreadData (process, pid), pid);
		}

		public override Process[] Threads {
			get {
				lock (this) {
					Process[] procs = new Process [thread_hash.Count];
					int i = 0;
					foreach (ThreadData data in thread_hash.Values)
						procs [i] = data.Process;
					return procs;
				}
			}
		}

		internal bool SignalHandler (Inferior inferior, int signal, out bool action)
		{
			Report.Debug (DebugFlags.Signals, "Signal: {0} {1} {2}",
				      has_threads, signal, restart_signal);

			if (signal == restart_signal) {
				if (has_threads)
					ReloadThreads (inferior);
				action = false;
				return true;
			}

			if (signal == cancel_signal) {
				action = false;
				return true;
			}

			if ((signal == inferior.SIGCHLD) || (signal == inferior.SIGPROF)) {
				inferior.SetSignal (0, false);
				action = false;
				return true;
			}

			if (signal == inferior.SIGINT) {
				inferior.SetSignal (0, false);
				action = true;
				return true;
			}

			if ((signal == inferior.SIGPWR) || (signal == inferior.SIGXCPU)) {
				inferior.SetSignal (0, false);
				action = false;
				return true;
			}

			if (signal != debug_signal) {
				action = true;
				return false;
			}

			ReloadThreads (inferior);

			inferior.SetSignal (restart_signal, false);
			action = false;
			return true;
		}

		internal override bool HandleChildEvent (Inferior inferior,
							 Inferior.ChildEvent cevent)
		{
			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument != 0)) {
				bool action;
				if (SignalHandler (inferior, (int) cevent.Argument, out action))
					return !action;
			}

			return false;
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		internal override void AcquireGlobalThreadLock (Inferior inferior, Process caller)
		{
			thread_lock_mutex.WaitOne ();
			if (thread_lock_level++ > 0)
				return;
			foreach (ThreadData data in thread_hash.Values) {
				Process process = data.Process;
				if ((process == caller) || (process.SingleSteppingEngine == null))
					continue;
				Register[] regs = process.SingleSteppingEngine.AcquireThreadLock ();
				int esp = (int)(long) regs [(int) I386Register.ESP].Data;
				TargetAddress addr = new TargetAddress (inferior.AddressDomain, esp);
				if (!data.Data.IsNull)
					inferior.WriteAddress (data.Data, addr);
			}
		}

		internal override void ReleaseGlobalThreadLock (Inferior inferior, Process caller)
		{
			if (--thread_lock_level > 0) {
				thread_lock_mutex.ReleaseMutex ();
				return;
			}
				
			foreach (ThreadData data in thread_hash.Values) {
				Process process = data.Process;
				if ((process == caller) || (process.SingleSteppingEngine == null))
					continue;
				process.SingleSteppingEngine.ReleaseThreadLock ();
			}
			thread_lock_mutex.ReleaseMutex ();
		}

		protected override void DoDispose ()
		{
			ThreadData[] threads = new ThreadData [thread_hash.Count];
			thread_hash.Values.CopyTo (threads, 0);
			for (int i = 0; i < threads.Length; i++)
				threads [i].Process.Dispose ();
			main_process.Dispose ();
			breakpoint_manager.Dispose ();
		}
	}
#endif
}
