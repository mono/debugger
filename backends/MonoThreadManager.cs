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
		TargetAddress mono_thread_notification;

		public TargetAddress Initialize (SingleSteppingEngine sse, Inferior inferior)
		{
			main_function = inferior.ReadGlobalAddress (info + 4);
			notification_address = inferior.ReadGlobalAddress (
				info + 4 + inferior.TargetAddressSize);

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

			main_function = reader.ReadGlobalAddress ();
			notification_address = reader.ReadGlobalAddress ();

			main_tid = reader.ReadInteger ();

			main_thread = reader.ReadGlobalAddress ();
			mono_thread_notification = reader.ReadGlobalAddress ();
		}

		int main_tid;
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
				sse.DaemonEventHandler = new DaemonEventHandler (managed_handler);
				return false;
			} else {
				sse.IsDaemon = true;
				return false;
			}
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
			esp += inferior.TargetAddressSize;
			TargetAddress data = inferior.ReadAddress (esp);

			ThreadData thread = (ThreadData) thread_hash [tid];
			sse.EndStackAddress = data;
			thread.IsManaged = true;
			thread.Engine = sse;
			thread.Data = data;

			if ((thread == null) || (thread.Engine != sse))
				throw new InternalError ();

			sse.Start (until, false);
			sse.DaemonEventHandler = null;
			return true;
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

			return false;
		}

		protected class ThreadData {
			public readonly int TID;
			public readonly int PID;
			public bool IsManaged;
			public SingleSteppingEngine Engine;
			public readonly TargetAddress StartStack;
			public TargetAddress Data;

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
	}
}
