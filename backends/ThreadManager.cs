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
		Process command_process;
		Mutex thread_lock_mutex;
		AddressDomain address_domain;
		BreakpointManager breakpoint_manager;
		ThreadGroup global_group;
		int thread_lock_level = 0;

		ManualResetEvent main_started_event;

		TargetAddress thread_handles = TargetAddress.Null;
		TargetAddress thread_handles_num = TargetAddress.Null;
		TargetAddress last_thread_event = TargetAddress.Null;
		bool initialized = false;
		bool reached_main = false;

		TargetAddress mono_thread_notification = TargetAddress.Null;
		TargetAddress mono_command_notification = TargetAddress.Null;
		TargetAddress thread_manager_notify_command = TargetAddress.Null;
		TargetAddress thread_manager_notify_tid = TargetAddress.Null;
		TargetAddress thread_manager_notify_data = TargetAddress.Null;
		TargetAddress thread_manager_background_pid = TargetAddress.Null;

		internal ThreadManager (DebuggerBackend backend, BfdContainer bfdc)
		{
			this.backend = backend;
			this.bfdc = bfdc;
			this.thread_hash = Hashtable.Synchronized (new Hashtable ());

			global_group = ThreadGroup.CreateThreadGroup ("global");

			thread_lock_mutex = new Mutex ();
			breakpoint_manager = new BreakpointManager ();

			main_started_event = new ManualResetEvent (false);

			address_domain = new AddressDomain ("global");
		}

		public bool Initialize (Process process, IInferior inferior)
		{
			this.main_process = process;
			add_process (process, inferior.PID, false);

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

		public Process[] Threads {
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

			if ((signal == PTraceInferior.SIGPWR) || (signal == PTraceInferior.SIGXCPU)) {
				inferior.SetSignal (0, false);
				action = false;
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

		public void Kill ()
		{
			if (main_process != null)
				main_process.Kill ();
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

				if (TargetExitedEvent != null)
					TargetExitedEvent ();
			}
		}

		void add_process (ThreadData data, int pid, bool send_event)
		{
			thread_hash.Add (pid, data);
			if (data.Process.State != TargetState.DAEMON)
				global_group.AddThread (data.Process);
			data.Process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
			if (send_event)
				OnThreadCreatedEvent (data.Process);
		}

		void add_process (Process process, int pid, bool send_event)
		{
			add_process (new ThreadData (process, pid), pid, send_event);
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
				else {
					new_process = main_process.CreateThread (pid);
					new_process.SetSignal (PTraceInferior.ThreadRestartSignal, true);
				}

				add_process (new_process, pid, true);
			}
		}

		protected enum ThreadManagerCommand {
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

		bool mono_thread_manager (DaemonThreadRunner runner, TargetAddress address, int signal)
		{
			if (!initialized) {
				TargetAddress maddr = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notification");
				TargetAddress caddr = bfdc.LookupSymbol ("MONO_DEBUGGER__command_notification");
				TargetAddress dpid = bfdc.LookupSymbol ("MONO_DEBUGGER__debugger_thread");
				TargetAddress mpid = bfdc.LookupSymbol ("MONO_DEBUGGER__main_pid");
				TargetAddress mthread = bfdc.LookupSymbol ("MONO_DEBUGGER__main_thread");
				TargetAddress cpid = bfdc.LookupSymbol ("MONO_DEBUGGER__command_thread");

				if (maddr.IsNull || caddr.IsNull || dpid.IsNull || mpid.IsNull ||
				    cpid.IsNull || mthread.IsNull)
					return false;

				thread_manager_notify_command = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_command");
				thread_manager_notify_tid = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_tid");
				thread_manager_notify_data = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_data");

				mono_thread_notification = runner.Inferior.ReadGlobalAddress (maddr);
				if (address != mono_thread_notification)
					return false;

				mono_command_notification = runner.Inferior.ReadGlobalAddress (caddr);

				int debugger_pid = runner.Inferior.ReadInteger (dpid);
				int main_pid = runner.Inferior.ReadInteger (mpid);
				TargetAddress main_thread = runner.Inferior.ReadGlobalAddress (mthread);
				int command_pid = runner.Inferior.ReadInteger (cpid);

				manager_process = runner.Process;
				add_process (manager_process, manager_process.PID, true);

				command_process = runner.Process.CreateDaemonThread (command_pid);
				add_process (command_process, command_pid, true);

				command_process.SingleSteppingEngine.Continue (true, false);

				debugger_process = backend.CreateDebuggerProcess (
					command_process, debugger_pid);
				add_process (debugger_process, debugger_pid, true);

				main_process = CreateThread (runner, main_pid, main_thread, true);

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

			int command = runner.Inferior.ReadInteger (thread_manager_notify_command);
			int pid = runner.Inferior.ReadInteger (thread_manager_notify_tid);
			TargetAddress data = runner.Inferior.ReadAddress (thread_manager_notify_data);

			ThreadData thread = (ThreadData) thread_hash [pid];

			switch ((ThreadManagerCommand) command) {
			case ThreadManagerCommand.ResumeThread:
				if (thread == null)
					throw new InternalError ();

				thread.Process.SingleSteppingEngine.Wait ();
				OnThreadCreatedEvent (thread.Process);
				return true;

			case ThreadManagerCommand.AcquireGlobalLock:
				if (thread == null)
					throw new InternalError ();

				AcquireGlobalThreadLock (runner.Inferior, thread.Process);
				return true;

			case ThreadManagerCommand.ReleaseGlobalLock:
				if (thread == null)
					throw new InternalError ();

				ReleaseGlobalThreadLock (runner.Inferior, thread.Process);
				return true;

			case ThreadManagerCommand.CreateThread:
				if (thread != null)
					throw new InternalError ();

				CreateThread (runner, pid, data, false);
				return true;

			default:
				throw new InternalError ();
			}
		}

		private Process CreateThread (DaemonThreadRunner runner, int pid, TargetAddress data,
					      bool is_main_thread)
		{
			int size = 3 * runner.Inferior.TargetIntegerSize +
				3 * runner.Inferior.TargetAddressSize;

			ITargetMemoryReader reader = runner.Inferior.ReadMemory (data, size);
			reader.ReadGlobalAddress ();
			int tid = reader.ReadInteger ();
			if (reader.ReadInteger () != pid)
				throw new InternalError ();
			int locked = reader.ReadInteger ();
			TargetAddress func = reader.ReadGlobalAddress ();
			TargetAddress start_stack = reader.ReadGlobalAddress ();

			Process process = runner.Process.CreateThread (pid);
			process.SetSignal (PTraceInferior.ThreadRestartSignal, true);

			ThreadData thread = new ThreadData (process, tid, pid, start_stack, data);
			add_process (thread, pid, false);

			if (is_main_thread)
				return process;

			if (func.Address == 0)
				throw new InternalError ("Created thread without start function");

			process.SingleSteppingEngine.Continue (func, false);
			return process;
		}

		internal DaemonThreadRunner StartManagedApplication (Process process, IInferior inferior,
								     ProcessStart start)
		{
			DaemonThreadHandler handler = new DaemonThreadHandler (mono_thread_manager);
			DaemonThreadRunner runner = new DaemonThreadRunner (
				backend, process, inferior, handler, start);

			process.TargetOutput += new TargetOutputHandler (inferior_output);
			process.DebuggerOutput += new DebuggerOutputHandler (debugger_output);
			process.DebuggerError += new DebuggerErrorHandler (debugger_error);

			main_started_event.WaitOne ();
			return runner;
		}

		internal Process StartApplication (DebuggerBackend backend, ProcessStart start,
						   BfdContainer bfd_container)
		{
			Process process = Process.StartApplication (backend, start, bfd_container);

			process.TargetOutput += new TargetOutputHandler (inferior_output);
			process.DebuggerOutput += new DebuggerOutputHandler (debugger_output);
			process.DebuggerError += new DebuggerErrorHandler (debugger_error);

			return process;
		}

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
		internal void AcquireGlobalThreadLock (IInferior inferior, Process caller)
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

		internal void ReleaseGlobalThreadLock (IInferior inferior, Process caller)
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
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				foreach (ThreadData data in thread_hash.Values)
					data.Process.Dispose ();
				breakpoint_manager.Dispose ();
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
