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
}
