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
using Mono.Debugger.Architecture;

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

		public TargetAddress Initialize (SingleSteppingEngine sse, Inferior inferior)
		{
			main_function = inferior.ReadGlobalAddress (info + 4);

			manager_sse = sse;
			new DaemonThreadRunner (sse, this.inferior,
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
		SingleSteppingEngine manager_sse;
		SingleSteppingEngine command_sse;
		SingleSteppingEngine debugger_sse;
		SingleSteppingEngine main_sse;
		DaemonThreadHandler debugger_handler;
		bool initialized;

		bool is_nptl;
		int first_index;

		public bool ThreadCreated (SingleSteppingEngine sse, Inferior inferior,
					   Inferior caller_inferior)
		{
			ThreadData tdata = new ThreadData (sse, inferior.TID, inferior.PID);
			thread_hash.Add (inferior.TID, tdata);

			if (thread_hash.Count == 1) {
				sse.IsDaemon = true;
				return false;
			}

			if (first_index == 0) {
				is_nptl = caller_inferior == this.inferior;
				first_index = is_nptl ? 2 : 3;
			}

			if (thread_hash.Count == first_index) {
				command_sse = sse;
				command_sse.IsDaemon = true;
				Report.Debug (DebugFlags.Threads,
					      "Created managed command sse: {0}",
					      sse);
				sse.DaemonEventHandler = new DaemonEventHandler (command_handler);
				return false;
			} else if (thread_hash.Count == first_index+1) {
				debugger_sse = sse;
				debugger_sse.IsDaemon = true;
				Report.Debug (DebugFlags.Threads,
					      "Created managed debugger sse: {0}",
					      sse);
				debugger_handler = thread_manager.DebuggerBackend.CreateDebuggerHandler (command_sse.Process);
				new DaemonThreadRunner (sse, inferior, debugger_handler);
				return false;
			} else if (thread_hash.Count == first_index+2) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed main sse: {0}",
					      sse);
				main_sse = sse;
				return true;
			} else if (thread_hash.Count > first_index+2) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed thread: {0}", sse);
				sse.DaemonEventHandler = new DaemonEventHandler (managed_handler);
				return false;
			} else {
				sse.IsDaemon = true;
				return false;
			}
		}

		bool command_handler (SingleSteppingEngine sse, Inferior inferior,
				      TargetEventArgs args)
		{
			Console.WriteLine ("COMMAND HANDLER: {0}", args);

			return true;
		}

		bool managed_handler (SingleSteppingEngine sse, Inferior inferior,
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
					   tid, until, sse);

			if ((thread == null) || (thread.Engine != sse))
				throw new InternalError ();

			sse.Start (until, false);
			sse.DaemonEventHandler = null;
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
			public readonly SingleSteppingEngine Engine;
			public readonly TargetAddress StartStack;
			public readonly TargetAddress Data;

			public ThreadData (SingleSteppingEngine sse, int tid, int pid,
					   TargetAddress start_stack, TargetAddress data)
			{
				this.IsManaged = true;
				this.Engine = sse;
				this.TID = tid;
				this.PID = pid;
				this.StartStack = start_stack;
				this.Data = data;
			}

			public ThreadData (SingleSteppingEngine sse, int pid, int tid)
			{
				this.IsManaged = false;
				this.Engine = sse;
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
