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
	internal class MonoThreadManager
	{
		ThreadManager thread_manager;
		Hashtable thread_hash;
		TargetAddress info;
		Inferior inferior;

		public static MonoThreadManager Initialize (ThreadManager thread_manager,
							    Inferior inferior)
		{
			TargetAddress info = inferior.SimpleLookup ("MONO_DEBUGGER__manager");
			if (info.IsNull)
				return null;

			return new MonoThreadManager (thread_manager, inferior, info);
		}

		protected MonoThreadManager (ThreadManager thread_manager, Inferior inferior,
					     TargetAddress info)
		{
			this.info = info;
			this.inferior = inferior;
			this.thread_manager = thread_manager;

			thread_hash = Hashtable.Synchronized (new Hashtable ());
		}

		TargetAddress main_function;
		TargetAddress main_thread;
		TargetAddress command_notification;
		TargetAddress mono_thread_notification;

		TargetAddress thread_manager_notify_command = TargetAddress.Null;
		TargetAddress thread_manager_notify_tid = TargetAddress.Null;

		public TargetAddress Initialize (NativeProcess process, Inferior inferior)
		{
			main_function = inferior.ReadGlobalAddress (info + 4);

			manager_process = process;
			new DaemonThreadRunner (process, this.inferior,
						new DaemonThreadHandler (main_handler));

			return main_function;
		}

		void do_initialize (Inferior inferior)
		{
			int size = inferior.ReadInteger (info);
			ITargetMemoryReader reader = inferior.ReadMemory (info, size);
			reader.ReadInteger ();

			main_function = reader.ReadGlobalAddress ();

			command_tid = reader.ReadInteger ();
			debugger_tid = reader.ReadInteger ();
			main_tid = reader.ReadInteger ();

			main_thread = reader.ReadGlobalAddress ();
			command_notification = reader.ReadGlobalAddress ();
			mono_thread_notification = reader.ReadGlobalAddress ();

			thread_manager_notify_command = reader.ReadGlobalAddress ();
			thread_manager_notify_tid = reader.ReadGlobalAddress ();
		}

		int command_tid;
		int debugger_tid;
		int main_tid;
		NativeProcess manager_process;
		NativeProcess command_process;
		NativeProcess debugger_process;
		NativeProcess main_process;
		DaemonThreadHandler debugger_handler;
		bool initialized;

		bool is_nptl;
		int first_index;

		public bool ThreadCreated (NativeProcess process, Inferior inferior,
					   Inferior caller_inferior)
		{
			ThreadData tdata = new ThreadData (process, inferior.TID, inferior.PID);
			thread_hash.Add (inferior.TID, tdata);

			if (thread_hash.Count == 1) {
				process.SetDaemonFlag ();
				return false;
			}

			if (first_index == 0) {
				is_nptl = caller_inferior == this.inferior;
				first_index = is_nptl ? 2 : 3;
			}

			if (thread_hash.Count == first_index) {
				command_process = process;
				command_process.SetDaemonFlag ();
				Report.Debug (DebugFlags.Threads,
					      "Created managed command process: {0}",
					      process);
				process.DaemonEventHandler = new DaemonEventHandler (command_handler);
				return false;
			} else if (thread_hash.Count == first_index+1) {
				debugger_process = process;
				debugger_process.SetDaemonFlag ();
				Report.Debug (DebugFlags.Threads,
					      "Created managed debugger process: {0}",
					      process);
				debugger_handler = thread_manager.DebuggerBackend.CreateDebuggerHandler (command_process);
				new DaemonThreadRunner (process, inferior, debugger_handler);
				return false;
			} else if (thread_hash.Count == first_index+2) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed main process: {0}",
					      process);
				main_process = process;
				return true;
			} else if (thread_hash.Count > first_index+3) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed thread: {0}", process);
				process.DaemonEventHandler = new DaemonEventHandler (managed_handler);
				return false;
			} else {
				process.SetDaemonFlag ();
				return false;
			}
		}

		bool command_handler (NativeProcess process, Inferior inferior,
				      TargetEventArgs args)
		{
			Console.WriteLine ("COMMAND HANDLER: {0}", args);

			return true;
		}

		bool managed_handler (NativeProcess process, Inferior inferior,
				      TargetEventArgs args)
		{
			if ((args.Type != TargetEventType.TargetStopped) ||
			    ((int) args.Data != 0))
				return false;

			if (inferior.CurrentFrame != mono_thread_notification)
				return false;

			TargetAddress esp = inferior.GetStackPointer ();
			esp += inferior.TargetAddressSize;
			int tid = inferior.ReadInteger (esp);
			esp += inferior.TargetIntegerSize;
			TargetAddress until = inferior.ReadAddress (esp);

			ThreadData thread = (ThreadData) thread_hash [tid];

			Console.WriteLine ("MONO THREAD MANAGER #1: {0:x} {1} {2}",
					   tid, until, process);

			if ((thread == null) || (thread.Process != process))
				throw new InternalError ();

			process.Start (until, false);
			process.DaemonEventHandler = null;
			return true;
		}

		bool main_handler (DaemonThreadRunner runner, TargetAddress address, int signal)
		{
			if (!initialized) {
				do_initialize (runner.Inferior);
				initialized = true;
			}

			if ((address != mono_thread_notification) || (signal != 0))
				return false;

			int command = runner.Inferior.ReadInteger (thread_manager_notify_command);
			int tid = runner.Inferior.ReadInteger (thread_manager_notify_tid);

			ThreadData thread = (ThreadData) thread_hash [tid];

			return true;
		}

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

			public ThreadData (Process process, int pid, int tid)
			{
				this.IsManaged = false;
				this.Process = process;
				this.TID = tid;
				this.PID = pid;
				this.StartStack = TargetAddress.Null;
				this.Data = TargetAddress.Null;
			}
		}

		protected enum ThreadManagerCommand {
			Unknown,
			CreateThread,
			ResumeThread,
			AcquireGlobalLock,
			ReleaseGlobalLock
		}
	}

#if FIXME
	internal class MonoThreadManager : ThreadManager
	{
		NativeThreadManager thread_manager;

		public MonoThreadManager (DebuggerBackend backend, BfdContainer bfdc)
			: base (backend, bfdc)
		{
			thread_manager = new NativeThreadManager (backend, bfdc, false);

			main_started_event = new ManualResetEvent (false);
			thread_hash = Hashtable.Synchronized (new Hashtable ());
		}

		public override AddressDomain AddressDomain {
			get { return thread_manager.AddressDomain; }
		}

		internal override Inferior CreateInferior (ProcessStart start)
		{
			return thread_manager.CreateInferior (start);
		}

#if FIXME
		public override Process StartApplication (ProcessStart start)
		{
			Inferior inferior = CreateInferior (start);

			Report.Debug (DebugFlags.Threads, "Starting managed application: {0}",
				      start);

			DaemonThreadHandler handler = new DaemonThreadHandler (daemon_handler);
			main_process = new ManagedWrapper (this, inferior, handler);
			main_process.Run ();

			Report.Debug (DebugFlags.Threads, "Done starting manager thread");

			main_started_event.WaitOne ();

			Report.Debug (DebugFlags.Threads,
				      "Done starting managed application: {0}", main_process);

			return main_process;
		}
#endif

		TargetAddress mono_thread_notification = TargetAddress.Null;
		TargetAddress mono_command_notification = TargetAddress.Null;
		TargetAddress thread_manager_notify_command = TargetAddress.Null;
		TargetAddress thread_manager_notify_tid = TargetAddress.Null;
		TargetAddress thread_manager_notify_data = TargetAddress.Null;
		TargetAddress thread_manager_background_pid = TargetAddress.Null;

		TargetAddress thread_manager_initialized = TargetAddress.Null;
		ManualResetEvent main_started_event;
		DaemonThreadHandler debugger_handler;

		Hashtable thread_hash;
		TargetAddress main_function = TargetAddress.Null;
		Process manager_process;
		Process command_process;
		Process debugger_process;

		bool mono_thread_manager_initialized = false;
		bool reached_main;
		bool has_threads;

		protected override void DoInitialize (Inferior inferior)
		{
			thread_manager.Initialize (inferior);

			thread_manager_notify_command = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_command");
			thread_manager_notify_tid = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_tid");
			thread_manager_notify_data = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notify_data");
			thread_manager_initialized = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_initialized");

			if (!thread_manager_notify_command.IsNull &&
			    !thread_manager_notify_tid.IsNull &&
			    !thread_manager_notify_data.IsNull &&
			    !thread_manager_initialized.IsNull)
				has_threads = true;
		}

		public bool Initialize (PTraceInferior inferior, TargetAddress address)
		{
			Initialize (inferior);

			TargetAddress maddr = bfdc.LookupSymbol ("MONO_DEBUGGER__thread_manager_notification");
			TargetAddress caddr = bfdc.LookupSymbol ("MONO_DEBUGGER__command_notification");
			TargetAddress dpid = bfdc.LookupSymbol ("MONO_DEBUGGER__debugger_thread");
			TargetAddress mpid = bfdc.LookupSymbol ("MONO_DEBUGGER__main_pid");
			TargetAddress mthread = bfdc.LookupSymbol ("MONO_DEBUGGER__main_thread");
			TargetAddress cpid = bfdc.LookupSymbol ("MONO_DEBUGGER__command_thread");
			TargetAddress mfunc = bfdc.LookupSymbol ("MONO_DEBUGGER__main_function");
			main_function = inferior.ReadGlobalAddress (mfunc);

			Report.Debug (DebugFlags.Threads,
				      "Mono thread manager: {0} - {1} {2} {3} {4} {5} {6}",
				      address, maddr, caddr, dpid, mpid, mthread, cpid);

			if (maddr.IsNull || caddr.IsNull || dpid.IsNull || mpid.IsNull ||
			    cpid.IsNull || mthread.IsNull)
				return false;

			mono_thread_notification = inferior.ReadGlobalAddress (maddr);
			if (address != mono_thread_notification)
				return false;

			mono_command_notification = inferior.ReadGlobalAddress (caddr);

			int debugger_pid = inferior.ReadInteger (dpid);
			int main_pid = inferior.ReadInteger (mpid);
			TargetAddress main_thread = inferior.ReadGlobalAddress (mthread);
			int command_pid = inferior.ReadInteger (cpid);

			manager_process = main_process;
			OnThreadCreatedEvent (manager_process);

			command_process = CreateThread (inferior, command_pid);
			OnThreadCreatedEvent (command_process);
			Report.Debug (DebugFlags.Threads, "Created command thread: {0}",
				      command_process);

			debugger_handler = backend.CreateDebuggerHandler (command_process);
			debugger_process = CreateThread (inferior, debugger_pid, debugger_handler);
			OnThreadCreatedEvent (debugger_process);

			Report.Debug (DebugFlags.Threads, "Created debugger thread: {0}",
				      debugger_process);

			main_process = CreateManagedThread (inferior, main_pid);
			Report.Debug (DebugFlags.Threads, "Created main thread: {0}",
				      main_process);

			return true;
		}

		protected Process CreateManagedThread (Inferior inferior, int pid)
		{
			Inferior new_inferior = inferior.CreateThread ();
			Process new_process = new NativeThread (new_inferior, pid);
			AddThread (inferior, new_process, pid, false);

			return new_process;
		}

		protected override void AddThread (Inferior inferior, Process new_process,
						   int pid, bool is_daemon)
		{
			thread_manager.AddThread (inferior, new_process, pid, is_daemon);
		}

		bool daemon_handler (DaemonThreadRunner runner, TargetAddress address, int signal)
		{
			bool action;
			PTraceInferior inferior = (PTraceInferior) runner.Inferior;
			if ((signal != 0) &&
			    thread_manager.SignalHandler (inferior, signal, out action)) {
				Console.WriteLine ("SIGNAL RETURNED: {0}", action);
				return action;
			}

			if (!mono_thread_manager_initialized) {
				has_threads = Initialize (inferior, address);
				mono_thread_manager_initialized = true;
				return has_threads;
			}

			if (!has_threads)
				return false;

			if (!reached_main) {
				backend.ReachedManagedMain (main_process);
				main_process.SingleSteppingEngine.Continue (main_function, true);
				OnMainThreadCreatedEvent (main_process);
				main_process.SingleSteppingEngine.ReachedMain (true);

				reached_main = true;
				main_started_event.Set ();
				return true;
			}

			Report.Debug (DebugFlags.Threads, "Done sending events");

			//
			// Already initialized.
			//

			if ((address != mono_thread_notification) || (signal != 0))
				return false;

			int command = runner.Inferior.ReadInteger (thread_manager_notify_command);
			int pid = runner.Inferior.ReadInteger (thread_manager_notify_tid);
			TargetAddress data = runner.Inferior.ReadAddress (thread_manager_notify_data);

			ThreadData thread = (ThreadData) thread_hash [pid];

			Console.WriteLine ("MONO THREAD MANAGER #3: {0} {1} {2} {3} {4}",
					   command, (ThreadManagerCommand) command, pid,
					   data, thread);

			switch ((ThreadManagerCommand) command) {
			case ThreadManagerCommand.Unknown:
				return true;

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

		Process CreateThread (DaemonThreadRunner runner, int pid, TargetAddress data,
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

			Console.WriteLine ("CREATE THREAD: {0} {1} {2} {3} {4}",
					   reader.BinaryReader.HexDump (), tid, locked, func, start_stack);

#if FIXME

			Process process = runner.Process.CreateThread (pid);
			process.SetSignal (runner.Inferior.ThreadRestartSignal, true);

			ThreadData thread = new ThreadData (process, tid, pid, start_stack, data);
			add_process (thread, pid, false);

			if (is_main_thread)
				return process;

			if (func.Address == 0)
				throw new InternalError ("Created thread without start function");

			process.SingleSteppingEngine.Continue (func, false);
			return process;
#endif

			throw new InternalError ();
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

		internal override void AcquireGlobalThreadLock (Inferior inferior, Process caller)
		{
			thread_manager.AcquireGlobalThreadLock (inferior, caller);
		}

		internal override void ReleaseGlobalThreadLock (Inferior inferior, Process caller)
		{
			thread_manager.ReleaseGlobalThreadLock (inferior, caller);
		}

		internal override bool HandleChildEvent (Inferior inferior,
							 Inferior.ChildEvent cevent)
		{
			return thread_manager.HandleChildEvent (inferior, cevent);
		}

		protected override void DoDispose ()
		{
			thread_manager.Dispose ();
		}

#if FIXME
		protected class ManagedWrapper : NativeProcess
		{
			MonoThreadManager thread_manager;
			DaemonThreadRunner runner;

			public ManagedWrapper (MonoThreadManager thread_manager,
					       Inferior inferior, DaemonThreadHandler handler)
				: base (inferior)
			{
				this.thread_manager = thread_manager;

				runner = new DaemonThreadRunner (this, inferior, -1, sse, handler);
				SetDaemonThreadRunner (runner);
			}
		}
#endif
	}
#endif
}
