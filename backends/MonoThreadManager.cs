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

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_set_notification (long address);

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
		TargetAddress notification_address;

		public TargetAddress Initialize (SingleSteppingEngine sse, Inferior inferior)
		{
			main_function = inferior.ReadGlobalAddress (info + 8);
			notification_address = inferior.ReadGlobalAddress (
				info + 8 + inferior.TargetAddressSize);

			mono_debugger_server_set_notification (notification_address.Address);

			manager_sse = sse;
			manager_sse.IsDaemon = true;

			return main_function;
		}

		void do_initialize (Inferior inferior)
		{
			int size = inferior.ReadInteger (info);
			ITargetMemoryReader reader = inferior.ReadMemory (info, size);
			reader.ReadInteger ();
			thread_size = reader.ReadInteger ();

			main_function = reader.ReadGlobalAddress ();
			notification_address = reader.ReadGlobalAddress ();

			main_tid = reader.ReadInteger ();

			main_thread = reader.ReadGlobalAddress ();

			thread_created (inferior, main_thread, true);
		}

		int main_tid;
		int thread_size;
		SingleSteppingEngine manager_sse;
		SingleSteppingEngine main_sse;
		ILanguageBackend csharp_language;
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
				Report.Debug (DebugFlags.Threads,
					      "Created managed main sse: {0}",
					      sse);
				csharp_language = thread_manager.DebuggerBackend.CreateDebuggerHandler ();
				main_sse = sse;
				return true;
			} else if (thread_hash.Count > first_index) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed thread: {0}", sse);
				return false;
			} else {
				sse.IsDaemon = true;
				return false;
			}
		}

		void thread_created (Inferior inferior, TargetAddress data, bool is_main)
		{
			ITargetMemoryReader reader = inferior.ReadMemory (data, thread_size);
			TargetAddress end_stack = reader.ReadAddress ();
			int tid = reader.BinaryReader.ReadInt32 ();
			int locked = reader.BinaryReader.ReadInt32 ();
			TargetAddress func = reader.ReadGlobalAddress ();
			TargetAddress start_stack = reader.ReadAddress ();

			ThreadData thread = (ThreadData) thread_hash [tid];
			thread.StartStack = start_stack;
			thread.Engine.EndStackAddress = data;

			if (!is_main)
				thread.Engine.Start (func, false);
		}

		internal bool HandleChildEvent (Inferior inferior,
						ref Inferior.ChildEvent cevent)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION) {
				NotificationType type = (NotificationType) cevent.Argument;

				switch (type) {
				case NotificationType.AcquireGlobalThreadLock: {
					int tid = (int) cevent.Data2;
					ThreadData thread = (ThreadData) thread_hash [tid];
					thread_manager.AcquireGlobalThreadLock (thread.Engine);
					break;
				}

				case NotificationType.ReleaseGlobalThreadLock: {
					int tid = (int) cevent.Data2;
					ThreadData thread = (ThreadData) thread_hash [tid];
					thread_manager.ReleaseGlobalThreadLock (thread.Engine);
					break;
				}

				case NotificationType.ThreadCreated: {
					int tid = (int) cevent.Data2;
					TargetAddress data = new TargetAddress (
						inferior.GlobalAddressDomain, cevent.Data1);

					thread_created (inferior, data, false);
					break;
				}

				case NotificationType.InitializeThreadManager:
					do_initialize (inferior);
					initialized = true;
					break;

				case NotificationType.WrapperMain:
					return true;

				case NotificationType.MainExited:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.CHILD_EXITED,
						0, 0, 0);
					return false;

				case NotificationType.UnhandledException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.UNHANDLED_EXCEPTION,
						0, cevent.Data1, cevent.Data2);
					return false;

				default: {
					TargetAddress data = new TargetAddress (
						inferior.GlobalAddressDomain, cevent.Data1);

					csharp_language.Notification (
						inferior, type, data, cevent.Data2);
					break;
				}
				}

				inferior.Continue ();
				return true;
			}

			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument == inferior.MonoThreadAbortSignal)) {
				Console.WriteLine ("THREAD ABORT: {0}", inferior.PID);

				inferior.SetSignal (inferior.SIGKILL, true);
				return false;

				cevent = new Inferior.ChildEvent (
					Inferior.ChildEventType.CHILD_EXITED,
					0, 0, 0);
				return false;
			}

			return false;
		}

		protected class ThreadData {
			public readonly int TID;
			public readonly int PID;
			public bool IsManaged;
			public SingleSteppingEngine Engine;
			public TargetAddress StartStack;

			public ThreadData (SingleSteppingEngine sse, int tid, int pid,
					   TargetAddress start_stack)
			{
				this.IsManaged = true;
				this.Engine = sse;
				this.TID = tid;
				this.PID = pid;
				this.StartStack = start_stack;
			}

			public ThreadData (SingleSteppingEngine sse, int tid, int pid)
			{
				this.IsManaged = false;
				this.Engine = sse;
				this.TID = tid;
				this.PID = pid;
				this.StartStack = TargetAddress.Null;
			}

			public override string ToString ()
			{
				return String.Format ("ThreadData ({0:x}:{1}:{2}:{3})",
						      TID, PID, IsManaged, StartStack);
			}
		}
	}
}
