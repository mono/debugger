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

// <summary>
// MonoThreadManager is a special case handler for thread events when
// we know we're running a managed app.
// </summary>

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
			manager_sse.Process.SetDaemon ();

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

			main_thread = reader.ReadGlobalAddress ();
			reader.ReadInteger (); /* main_tid */

			thread_created (inferior, main_thread, true);
		}

		int thread_size;
		SingleSteppingEngine manager_sse;
		ILanguageBackend csharp_language;

		bool is_nptl;
		int first_index;

		// These two constants represent the index of the first
		// *managed* thread started by the runtime.  In the NPTL
		// case, it is the third thread started, in the non-NPTL
		// case, it's the fourth.
		const int NPTL_FIRST_MANAGED_INDEX = 3;
		const int NON_NPTL_FIRST_MANAGED_INDEX = 4;

		public bool ThreadCreated (SingleSteppingEngine sse, Inferior inferior,
					   Inferior caller_inferior)
		{
			ThreadData tdata = new ThreadData (sse, inferior.TID, inferior.PID);
			thread_hash.Add (inferior.TID, tdata);

			if (thread_hash.Count == 1) {
				sse.Process.SetDaemon ();
				return false;
			}

			if (first_index == 0) {
				is_nptl = caller_inferior == this.inferior;
				first_index = is_nptl ? NPTL_FIRST_MANAGED_INDEX : NON_NPTL_FIRST_MANAGED_INDEX;
			}

			if (thread_hash.Count == first_index) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed main sse: {0}",
					      sse);
				csharp_language = thread_manager.DebuggerBackend.CreateDebuggerHandler ();
				return true;
			} else if (thread_hash.Count > first_index) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed thread: {0}", sse);
				return false;
			} else {
				sse.Process.SetDaemon ();
				return false;
			}
		}

		void thread_created (Inferior inferior, TargetAddress data, bool is_main)
		{
			ITargetMemoryReader reader = inferior.ReadMemory (data, thread_size);
			reader.ReadAddress ();
			int tid = reader.BinaryReader.ReadInt32 ();
			reader.BinaryReader.ReadInt32 ();
			TargetAddress func = reader.ReadGlobalAddress ();
			TargetAddress start_stack = reader.ReadAddress ();

			ThreadData thread = (ThreadData) thread_hash [tid];
			thread.StartStack = start_stack;
			thread.Engine.EndStackAddress = data;

			if (!is_main)
				thread.Engine.Start (func, false);
		}

		void thread_abort (Inferior inferior, int tid)
		{
			Report.Debug (DebugFlags.Threads, "Aborting thread {0:x}", tid);

			ThreadData thread = (ThreadData) thread_hash [tid];
			thread_hash.Remove (tid);

			inferior.Continue ();
			thread_manager.KillThread (thread.Engine);
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
					TargetAddress data = new TargetAddress (
						inferior.GlobalAddressDomain, cevent.Data1);

					thread_created (inferior, data, false);
					break;
				}

				case NotificationType.ThreadAbort: {
					int tid = (int) cevent.Data2;
					thread_abort (inferior, tid);
					return true;
				}

				case NotificationType.InitializeThreadManager:
					do_initialize (inferior);
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

				case NotificationType.HandleException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.HANDLE_EXCEPTION,
						0, cevent.Data1, cevent.Data2);
					return false;

				case NotificationType.ThrowException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.THROW_EXCEPTION,
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
