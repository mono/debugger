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

namespace Mono.Debugger.Backends
{

// <summary>
// MonoThreadManager is a special case handler for thread events when
// we know we're running a managed app.
// </summary>

	internal class MonoThreadManager
	{
		ThreadManager thread_manager;
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
		}

		TargetAddress main_function;
		TargetAddress main_thread;
		TargetAddress notification_address;

		public TargetAddress Initialize (SingleSteppingEngine sse, Inferior inferior)
		{
			main_function = inferior.ReadAddress (info + 8);
			notification_address = inferior.ReadAddress (
				info + 8 + inferior.TargetAddressSize);

			mono_debugger_server_set_notification (notification_address.Address);

			manager_sse = sse;
			manager_sse.Process.SetDaemon ();

			return main_function;
		}

		void do_initialize (SingleSteppingEngine engine, Inferior inferior)
		{
			int size = inferior.ReadInteger (info);
			TargetBlob blob = inferior.ReadMemory (info, size);
			TargetReader reader = new TargetReader (blob.Contents, inferior);

			reader.ReadInteger ();
			thread_size = reader.ReadInteger ();

			main_function = reader.ReadAddress ();
			notification_address = reader.ReadAddress ();

			main_thread = reader.ReadAddress ();
			reader.ReadInteger (); /* main_tid */

			thread_created (engine, inferior, main_thread, true);
		}

		int thread_size;
		SingleSteppingEngine manager_sse;
		ILanguageBackend csharp_language;

		bool is_nptl;
		int first_index;
		int index;

		// These two constants represent the index of the first
		// *managed* thread started by the runtime.  In the NPTL
		// case, it is the third thread started, in the non-NPTL
		// case, it's the fourth.
		const int NPTL_FIRST_MANAGED_INDEX = 3;
		const int NON_NPTL_FIRST_MANAGED_INDEX = 4;

		public bool ThreadCreated (SingleSteppingEngine sse, Inferior inferior,
					   Inferior caller_inferior)
		{
			++index;

			if (index == 1) {
				sse.Process.SetDaemon ();
				return false;
			}

			if (first_index == 0) {
				is_nptl = caller_inferior == this.inferior;
				first_index = is_nptl ? NPTL_FIRST_MANAGED_INDEX : NON_NPTL_FIRST_MANAGED_INDEX;
			}

			if (index == first_index) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed main sse: {0}",
					      sse);
				csharp_language = thread_manager.Debugger.CreateDebuggerHandler ();
				return true;
			} else if (index > first_index) {
				Report.Debug (DebugFlags.Threads,
					      "Created managed thread: {0}", sse);
				return false;
			} else {
				sse.Process.SetDaemon ();
				return false;
			}
		}

		void thread_created (SingleSteppingEngine engine, Inferior inferior,
				     TargetAddress data, bool is_main)
		{
			TargetBlob blob = inferior.ReadMemory (data, thread_size);
			TargetReader reader = new TargetReader (blob.Contents, inferior);
			reader.ReadAddress ();
			reader.ReadAddress ();
			TargetAddress func = reader.ReadAddress ();

			engine.EndStackAddress = data;

			if (!is_main)
				engine.Start (func, false);
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref Inferior.ChildEvent cevent)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION) {
				NotificationType type = (NotificationType) cevent.Argument;

				switch (type) {
				case NotificationType.AcquireGlobalThreadLock:
					thread_manager.AcquireGlobalThreadLock (engine);
					break;

				case NotificationType.ReleaseGlobalThreadLock:
					thread_manager.ReleaseGlobalThreadLock (engine);
					break;

				case NotificationType.ThreadCreated: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);

					thread_created (engine, inferior, data, false);
					break;
				}

				case NotificationType.ThreadAbort:
					break;

				case NotificationType.InitializeThreadManager:
					do_initialize (engine, inferior);
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
						inferior.AddressDomain, cevent.Data1);

					Console.WriteLine ("NOTIFICATION: {0} {1}", type, data);

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
	}
}
