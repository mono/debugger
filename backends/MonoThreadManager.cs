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
		MonoDebuggerInfo debugger_info;
		Hashtable engine_hash;
		Inferior inferior;

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_set_notification (long address);

		public static MonoThreadManager Initialize (ThreadManager thread_manager,
							    Inferior inferior, bool attach)
		{
			TargetAddress info = inferior.SimpleLookup ("MONO_DEBUGGER__debugger_info");
			if (info.IsNull)
				return null;

			return new MonoThreadManager (thread_manager, inferior, info, attach);
		}

		protected MonoThreadManager (ThreadManager thread_manager, Inferior inferior,
					     TargetAddress info, bool attach)
		{
			this.inferior = inferior;
			this.thread_manager = thread_manager;

			this.engine_hash = Hashtable.Synchronized (new Hashtable ());

			TargetBinaryReader header = inferior.ReadMemory (info, 16).GetReader ();
			long magic = header.ReadInt64 ();
			if (magic != MonoDebuggerInfo.DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInt32 ();
			if (version < MonoDebuggerInfo.MinDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at least {1}.", version,
					MonoDebuggerInfo.MinDynamicVersion);
			if (version > MonoDebuggerInfo.MaxDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at most {1}.", version,
					MonoDebuggerInfo.MaxDynamicVersion);

			int size = header.ReadInt32 ();

			TargetReader reader = new TargetReader (
				inferior.ReadMemory (info, size), inferior);
			debugger_info = new MonoDebuggerInfo (reader);

			if (attach)
				initialize_notifications (inferior);
			else
				notification_bpt = inferior.BreakpointManager.InsertBreakpoint (
					inferior, new InitializeBreakpoint (this),
					debugger_info.Initialize);
		}

		int notification_bpt;

		protected void initialize_notifications (Inferior inferior)
		{
			TargetAddress notification = inferior.ReadAddress (
				debugger_info.NotificationAddress);

			mono_debugger_server_set_notification (notification.Address);

			inferior.BreakpointManager.RemoveBreakpoint (inferior, notification_bpt);
		}

		[Serializable]
		protected class InitializeBreakpoint : Breakpoint
		{
			protected readonly MonoThreadManager manager;

			public InitializeBreakpoint (MonoThreadManager manager)
				: base ("initialize", null)
			{
				this.manager = manager;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				manager.initialize_notifications (inferior);
				remain_stopped = false;
				return true;
			}

			public override Breakpoint Clone ()
			{
				return new InitializeBreakpoint (manager);
			}
		}

		TargetAddress main_function;
		TargetAddress main_thread;
		ILanguageBackend csharp_language;

		int index;

		public bool ThreadCreated (SingleSteppingEngine sse, Inferior inferior,
					   Inferior caller_inferior)
		{
			engine_hash.Add (sse.TID, sse);

			++index;
			if (index < 3)
				sse.Thread.SetDaemon ();

			return false;
		}

		void thread_created (SingleSteppingEngine engine, Inferior inferior,
				     TargetAddress data, long tid)
		{
			engine = (SingleSteppingEngine) engine_hash [tid];
			engine.EndStackAddress = data;
		}

		public void Attach (SingleSteppingEngine main_engine, CommandResult[] results)
		{
			foreach (CommandResult result in results)
				result.Wait ();
			main_engine.Attach (debugger_info);
		}

		public CommandResult GetThreadID (Thread thread)
		{
			return thread.GetThreadID (this, debugger_info);
		}

		internal void SetThreadId (SingleSteppingEngine engine)
		{
			engine_hash.Add (engine.TID, engine);
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref Inferior.ChildEvent cevent)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION) {
				NotificationType type = (NotificationType) cevent.Argument;

				Report.Debug (DebugFlags.EventLoop,
					      "{0} received notification {1}: {2}",
					      engine, type, cevent);

				switch (type) {
				case NotificationType.AcquireGlobalThreadLock:
					engine.Process.AcquireGlobalThreadLock (engine);
					break;

				case NotificationType.ReleaseGlobalThreadLock:
					engine.Process.ReleaseGlobalThreadLock (engine);
					break;

				case NotificationType.ThreadCreated: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);

					thread_created (engine, inferior, data, cevent.Data2);
					break;
				}

				case NotificationType.ThreadAbort:
					break;

				case NotificationType.InitializeThreadManager:
					if (!engine_hash.Contains (cevent.Data1))
						engine_hash.Add (cevent.Data1, engine);
					csharp_language = inferior.Process.CreateDebuggerHandler (
						debugger_info);
					break;

				case NotificationType.ReachedMain: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);

					engine.ReachedMain (data);

					inferior.Process.ReachedMain (inferior, engine.Thread, engine);
					return true;
				}

				case NotificationType.WrapperMain:
				case NotificationType.MainExited:
					break;

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

	// <summary>
	//   This class is the managed representation of the MONO_DEBUGGER__debugger_info struct.
	//   as defined in debugger/wrapper/mono-debugger-jit-wrapper.h
	// </summary>
	internal class MonoDebuggerInfo
	{
		// These constants must match up with those in mono/mono/metadata/mono-debug.h
		public const int  MinDynamicVersion = 54;
		public const int  MaxDynamicVersion = 54;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		public readonly TargetAddress NotificationAddress;
		public readonly TargetAddress MonoTrampolineCode;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress MetadataInfo;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress GetVirtualMethod;
		public readonly TargetAddress GetBoxedObjectMethod;
		public readonly TargetAddress InsertBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupClass;
		public readonly TargetAddress LookupType;
		public readonly TargetAddress LookupAssembly;
		public readonly TargetAddress RunFinally;
		public readonly TargetAddress GetThreadId;
		public readonly TargetAddress Attach;
		public readonly TargetAddress Initialize;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			/* skip past magic, version, and total_size */
			reader.Offset = 16;

			SymbolTableSize         = reader.ReadInteger ();

			reader.Offset = 24;
			NotificationAddress     = reader.ReadAddress ();
			MonoTrampolineCode      = reader.ReadAddress ();
			SymbolTable             = reader.ReadAddress ();
			MetadataInfo            = reader.ReadAddress ();
			CompileMethod           = reader.ReadAddress ();
			GetVirtualMethod        = reader.ReadAddress ();
			GetBoxedObjectMethod    = reader.ReadAddress ();
			InsertBreakpoint        = reader.ReadAddress ();
			RemoveBreakpoint        = reader.ReadAddress ();
			RuntimeInvoke           = reader.ReadAddress ();
			CreateString            = reader.ReadAddress ();
			ClassGetStaticFieldData = reader.ReadAddress ();
			LookupClass             = reader.ReadAddress ();
			LookupType              = reader.ReadAddress ();
			LookupAssembly          = reader.ReadAddress ();
			RunFinally              = reader.ReadAddress ();
			GetThreadId             = reader.ReadAddress ();
			Attach                  = reader.ReadAddress ();
			Initialize              = reader.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x})",
				MonoTrampolineCode, SymbolTable, SymbolTableSize,
				CompileMethod, InsertBreakpoint, RemoveBreakpoint,
				RuntimeInvoke);
		}
	}
}
