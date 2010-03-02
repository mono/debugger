using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend.Mono
{

// <summary>
// MonoThreadManager is a special case handler for thread events when
// we know we're running a managed app.
// </summary>

	internal enum NotificationType {
		InitializeManagedCode	= 1,
		InitializeCorlib,
		JitBreakpoint,
		InitializeThreadManager,
		AcquireGlobalThreadLock,
		ReleaseGlobalThreadLock,
		WrapperMain,
		MainExited,
		UnhandledException,
		ThrowException,
		HandleException,
		ThreadCreated,
		ThreadCleanup,
		GcThreadCreated,
		GcThreadExited,
		ReachedMain,
		FinalizeManagedCode,
		LoadModule,
		UnloadModule,
		DomainCreate,
		DomainUnload,
		ClassInitialized,
		InterruptionRequest,
		CreateAppDomain,
		UnloadAppDomain,

		OldTrampoline	= 256,
		Trampoline	= 512
	}

	internal enum ThreadFlags {
		None = 0,
		Internal = 1,
		ThreadPool = 2
	};

	internal delegate bool ManagedCallbackFunction (SingleSteppingEngine engine);

	internal class MonoThreadManager
	{
		ProcessServant process;
		MonoDebuggerInfo debugger_info;

		protected MonoThreadManager (ProcessServant process, Inferior inferior,
					     MonoDebuggerInfo debugger_info)
		{
			this.process = process;
			this.debugger_info = debugger_info;

			inferior.WriteInteger (debugger_info.UsingMonoDebugger, 1);

			notification_bpt = new InitializeBreakpoint (this, debugger_info.Initialize);
			notification_bpt.Insert (inferior);
		}

		public static MonoThreadManager Initialize (ProcessServant process, Inferior inferior,
							    TargetAddress info, bool attach)
		{
			MonoDebuggerInfo debugger_info = MonoDebuggerInfo.Create (inferior, info);
			if (debugger_info == null)
				return null;

			if (attach) {
				if (!debugger_info.CheckRuntimeVersion (81, 2)) {
					Report.Error ("The Mono runtime of the target application is too old to support attaching,\n" +
						      "attaching as a native application.");
					return null;
				}

				if ((debugger_info.RuntimeFlags & 1) != 1) {
					Report.Error ("The Mono runtime of the target application does not support attaching,\n" +
						      "attaching as a native application.");
					return null;
				}
			}

			return new MonoThreadManager (process, inferior, debugger_info);
		}

		AddressBreakpoint notification_bpt;
		IntPtr mono_runtime_info;
		int debugger_version;

		internal bool HasCodeBuffer {
			get;
			private set;
		}

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize_mono_runtime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_finalize_mono_runtime (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_initialize_code_buffer (
			IntPtr runtime, long executable_code_buffer,
			int executable_code_buffer_size);

		protected void initialize_notifications (Inferior inferior)
		{
			TargetAddress executable_code_buffer = inferior.ReadAddress (
				debugger_info.ExecutableCodeBuffer);
			HasCodeBuffer = !executable_code_buffer.IsNull;

			mono_runtime_info = mono_debugger_server_initialize_mono_runtime (
				inferior.TargetAddressSize,
				debugger_info.NotificationAddress.Address,
				executable_code_buffer.Address,
				debugger_info.ExecutableCodeBufferSize,
				debugger_info.BreakpointInfo.Address,
				debugger_info.BreakpointInfoIndex.Address,
				debugger_info.BreakpointArraySize);
			inferior.SetRuntimeInfo (mono_runtime_info);

			debugger_version = inferior.ReadInteger (debugger_info.DebuggerVersion);

			if (notification_bpt != null) {
				notification_bpt.Remove (inferior);
				notification_bpt = null;
			}
		}

		internal void InitCodeBuffer (Inferior inferior, TargetAddress code_buffer)
		{
			HasCodeBuffer = true;
			mono_debugger_server_initialize_code_buffer (
				mono_runtime_info, code_buffer.Address,
				debugger_info.ExecutableCodeBufferSize);
		}

		protected class InitializeBreakpoint : AddressBreakpoint
		{
			protected readonly MonoThreadManager manager;

			public InitializeBreakpoint (MonoThreadManager manager, TargetAddress address)
				: base ("initialize", ThreadGroup.System, address)
			{
				this.manager = manager;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				return true;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				manager.initialize_notifications (inferior);
				remain_stopped = false;
				return true;
			}
		}

		internal bool InitializeAfterAttach (Inferior inferior)
		{
			initialize_notifications (inferior);

			inferior.WriteAddress (debugger_info.ThreadVTablePtr,
					       debugger_info.ThreadVTable);
			inferior.WriteAddress (debugger_info.EventHandlerPtr,
					       debugger_info.EventHandler);
			inferior.WriteInteger (debugger_info.UsingMonoDebugger, 1);

			csharp_language = inferior.Process.CreateMonoLanguage (debugger_info);
			csharp_language.InitializeAttach (inferior);

			return true;
		}

		internal void Detach (Inferior inferior)
		{
			inferior.WriteAddress (debugger_info.ThreadVTablePtr, TargetAddress.Null);
			inferior.WriteAddress (debugger_info.EventHandler, TargetAddress.Null);
			inferior.WriteInteger (debugger_info.UsingMonoDebugger, 0);
		}

		internal void AddManagedCallback (Inferior inferior, ManagedCallbackData data)
		{
			inferior.WriteInteger (MonoDebuggerInfo.InterruptionRequest, 1);
			managed_callbacks.Enqueue (data);
		}

		internal Queue<ManagedCallbackData> ClearManagedCallbacks (Inferior inferior)
		{
			inferior.WriteInteger (MonoDebuggerInfo.InterruptionRequest, 0);
			Queue<ManagedCallbackData> retval = managed_callbacks;
			managed_callbacks = new Queue<ManagedCallbackData> ();
			return retval;
		}

		TargetAddress main_function;
		TargetAddress main_thread;
		MonoLanguageBackend csharp_language;
		Queue<ManagedCallbackData> managed_callbacks = new Queue<ManagedCallbackData> ();

		internal bool CanExecuteCode {
			get { return mono_runtime_info != IntPtr.Zero; }
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get { return debugger_info; }
		}

		int index;
		internal void ThreadCreated (SingleSteppingEngine sse)
		{
			sse.Inferior.SetRuntimeInfo (mono_runtime_info);
			if (!MonoDebuggerInfo.CheckRuntimeVersion (81, 3) && !process.IsAttached) {
				if (++index < 3)
					sse.Thread.ThreadFlags |= Thread.Flags.Daemon | Thread.Flags.Immutable;
			} else {
				sse.Thread.ThreadFlags |= Thread.Flags.Daemon | Thread.Flags.Immutable;
			}
		}

		void check_thread_flags (SingleSteppingEngine engine, ThreadFlags flags)
		{
			if ((flags & (ThreadFlags.Internal | ThreadFlags.ThreadPool)) != ThreadFlags.Internal) {
				engine.Thread.ThreadFlags &= ~(Thread.Flags.Daemon | Thread.Flags.Immutable);
				if (engine != process.MainThread)
					process.Debugger.Client.OnManagedThreadCreatedEvent (engine.Thread);
			} else if ((flags & ThreadFlags.ThreadPool) != 0) {
				engine.Thread.ThreadFlags &= ~Thread.Flags.Immutable;
			}
		}

		internal void InitializeThreads (Inferior inferior)
		{
			TargetAddress ptr = inferior.ReadAddress (MonoDebuggerInfo.ThreadTable);
			while (!ptr.IsNull) {
				int size;
				if (MonoDebuggerInfo.CheckRuntimeVersion (81, 3))
					size = 60 + inferior.TargetMemoryInfo.TargetAddressSize;
				else
					size = 32 + inferior.TargetMemoryInfo.TargetAddressSize;
				TargetReader reader = new TargetReader (inferior.ReadMemory (ptr, size));

				long tid = reader.ReadLongInteger ();
				TargetAddress lmf_addr = reader.ReadAddress ();
				TargetAddress end_stack = reader.ReadAddress ();

				TargetAddress extended_notifications_addr = ptr + 24;

				if (inferior.TargetMemoryInfo.TargetAddressSize == 4)
					tid &= 0x00000000ffffffffL;

				reader.Offset += 8;
				ptr = reader.ReadAddress ();

				ThreadFlags flags = ThreadFlags.None;
				if (MonoDebuggerInfo.CheckRuntimeVersion (81, 3)) {
					reader.Offset = 56 + inferior.TargetAddressSize;
					flags = (ThreadFlags) reader.ReadInteger ();
				}

				bool found = false;
				foreach (SingleSteppingEngine engine in process.Engines) {
					if (engine.TID != tid)
						continue;

					engine.SetManagedThreadData (lmf_addr, extended_notifications_addr);
					engine.OnManagedThreadCreated (end_stack);
					check_thread_flags (engine, flags);
					found = true;
					break;
				}

				if (!found)
					Report.Error ("Cannot find thread {0:x} in {1}",
						      tid, process.ProcessStart.CommandLine);
			}
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref Inferior.ChildEvent cevent, out bool resume_target)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION) {
				NotificationType type = (NotificationType) cevent.Argument;

				Report.Debug (DebugFlags.EventLoop,
					      "{0} received notification {1}: {2}",
					      engine, type, cevent);

				switch (type) {
				case NotificationType.AcquireGlobalThreadLock:
					Report.Debug (DebugFlags.Threads,
						      "{0} received notification {1}", engine, type);
					engine.ProcessServant.AcquireGlobalThreadLock (engine);
					break;

				case NotificationType.ReleaseGlobalThreadLock:
					Report.Debug (DebugFlags.Threads,
						      "{0} received notification {1}", engine, type);
					engine.ProcessServant.ReleaseGlobalThreadLock (engine);
					break;

				case NotificationType.ThreadCreated: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data2);

					TargetAddress lmf = inferior.ReadAddress (data + 8);
					engine.SetManagedThreadData (lmf, data + 24);

					if (MonoDebuggerInfo.CheckRuntimeVersion (81, 3)) {
						int flags_offset = 56 + inferior.TargetAddressSize;
						ThreadFlags flags = (ThreadFlags) inferior.ReadInteger (data + flags_offset);
						check_thread_flags (engine, flags);
					}

					Report.Debug (DebugFlags.Threads,
						      "{0} managed thread created: {1:x} {2} {3} - {4}",
						      engine, cevent.Data1, data, lmf, engine.LMFAddress);
					break;
				}

				case NotificationType.ThreadCleanup: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);

					Report.Debug (DebugFlags.Threads,
						      "{0} managed thread cleanup: {1:x} {2}",
						      engine, cevent.Data2, data);
					break;
				}

				case NotificationType.GcThreadCreated: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);
					long tid = cevent.Data2;

					Report.Debug (DebugFlags.Threads,
						      "{0} created gc thread: {1:x} {2}",
						      engine, tid, data);

					engine = engine.ProcessServant.GetEngineByTID (inferior, tid);
					if (engine == null)
						throw new InternalError ();

					engine.OnManagedThreadCreated (data);
					break;
				}

				case NotificationType.GcThreadExited:
					Report.Debug (DebugFlags.Threads, "{0} gc thread exited", engine);
					engine.OnManagedThreadExited ();
					try {
						inferior.Continue ();
					} catch {
						// Ignore errors; for some reason, the thread may have died
						// already by the time get this notification.
					}
					resume_target = false;
					return true;

				case NotificationType.InitializeThreadManager:
					csharp_language = inferior.Process.CreateMonoLanguage (
						debugger_info);
					if (engine.ProcessServant.IsAttached)
						csharp_language.InitializeAttach (inferior);
					else
						csharp_language.Initialize (inferior);

					break;

				case NotificationType.ReachedMain: {
					Inferior.StackFrame iframe = inferior.GetCurrentFrame (false);
					engine.SetMainReturnAddress (iframe.StackPointer);
					engine.ProcessServant.OnProcessReachedMainEvent ();
					resume_target = !engine.InitializeBreakpoints ();
					return true;
				}

				case NotificationType.WrapperMain:
					break;
				case NotificationType.MainExited:
					engine.SetMainReturnAddress (TargetAddress.Null);
					break;

				case NotificationType.UnhandledException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.UNHANDLED_EXCEPTION,
						0, cevent.Data1, cevent.Data2);
					resume_target = false;
					return false;

				case NotificationType.HandleException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.HANDLE_EXCEPTION,
						0, cevent.Data1, cevent.Data2);
					resume_target = false;
					return false;

				case NotificationType.ThrowException:
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.THROW_EXCEPTION,
						0, cevent.Data1, cevent.Data2);
					resume_target = false;
					return false;

				case NotificationType.FinalizeManagedCode:
					mono_debugger_server_finalize_mono_runtime (mono_runtime_info);
					mono_runtime_info = IntPtr.Zero;
					csharp_language = null;
					break;

				case NotificationType.OldTrampoline:
				case NotificationType.Trampoline:
					resume_target = false;
					return false;

				case NotificationType.ClassInitialized:
					break;

				case NotificationType.InterruptionRequest:
					inferior.WriteInteger (MonoDebuggerInfo.InterruptionRequest, 0);
					var callbacks = managed_callbacks;
					managed_callbacks = new Queue<ManagedCallbackData> ();
					resume_target = !engine.OnManagedCallback (callbacks);
					return true;

				default: {
					TargetAddress data = new TargetAddress (
						inferior.AddressDomain, cevent.Data1);

					resume_target = csharp_language.Notification (
						engine, inferior, type, data, cevent.Data2);
					return true;
				}
				}

				resume_target = true;
				return true;
			}

			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument == inferior.MonoThreadAbortSignal)) {
				resume_target = true;
				return true;
			}

			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) && (cevent.Argument != 0) && !
			    engine.Process.Session.Config.StopOnManagedSignals) {
				if (inferior.IsManagedSignal ((int) cevent.Argument)) {
					resume_target = true;
					return true;
				}
			}

			resume_target = false;
			return false;
		}
	}

	// <summary>
	//   This class is the managed representation of the MONO_DEBUGGER__debugger_info struct.
	//   as defined in mono/mini/debug-debugger.h
	// </summary>
	internal class MonoDebuggerInfo
	{
		// These constants must match up with those in mono/mono/metadata/mono-debug.h
		public const int  MinDynamicVersion = 80;
		public const int  MaxDynamicVersion = 81;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		public readonly int MajorVersion;
		public readonly int MinorVersion;

		public readonly int RuntimeFlags;

		public readonly int MonoTrampolineNum;
		public readonly TargetAddress MonoTrampolineCode;
		public readonly TargetAddress NotificationAddress;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress MonoMetadataInfo;
		public readonly TargetAddress DebuggerVersion;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress GetVirtualMethod;
		public readonly TargetAddress GetBoxedObjectMethod;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupClass;
		public readonly TargetAddress RunFinally;
		public readonly TargetAddress InsertMethodBreakpoint;
		public readonly TargetAddress InsertSourceBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RegisterClassInitCallback;
		public readonly TargetAddress RemoveClassInitCallback;
		public readonly TargetAddress Initialize;
		public readonly TargetAddress ThreadTable;
		public readonly TargetAddress ExecutableCodeBuffer;
		public readonly TargetAddress BreakpointInfo;
		public readonly TargetAddress BreakpointInfoIndex;
		public readonly int ExecutableCodeBufferSize;
		public readonly int BreakpointArraySize;
		public readonly TargetAddress GetMethodSignature;
		public readonly TargetAddress InitCodeBuffer;

		public readonly TargetAddress ThreadVTablePtr;
		public readonly TargetAddress ThreadVTable;
		public readonly TargetAddress EventHandlerPtr;
		public readonly TargetAddress EventHandler;

		public readonly TargetAddress UsingMonoDebugger;
		public readonly TargetAddress InterruptionRequest;

		public readonly TargetAddress AbortRuntimeInvoke = TargetAddress.Null;

		public static MonoDebuggerInfo Create (TargetMemoryAccess memory, TargetAddress info)
		{
			TargetBinaryReader header = memory.ReadMemory (info, 24).GetReader ();
			long magic = header.ReadInt64 ();
			if (magic != DynamicMagic) {
				Report.Error ("`MONO_DEBUGGER__debugger_info' at {0} has unknown magic {1:x}.", info, magic);
				return null;
			}

			int version = header.ReadInt32 ();
			if (version < MinDynamicVersion) {
				Report.Error ("`MONO_DEBUGGER__debugger_info' has version {0}, " +
					      "but expected at least {1}.", version,
					      MonoDebuggerInfo.MinDynamicVersion);
				return null;
			}
			if (version > MaxDynamicVersion) {
				Report.Error ("`MONO_DEBUGGER__debugger_info' has version {0}, " +
					      "but expected at most {1}.", version,
					      MonoDebuggerInfo.MaxDynamicVersion);
				return null;
			}

			header.ReadInt32 (); // minor version
			header.ReadInt32 ();

			int size = header.ReadInt32 ();

			TargetReader reader = new TargetReader (memory.ReadMemory (info, size));
			return new MonoDebuggerInfo (memory, reader);
		}

		public bool CheckRuntimeVersion (int major, int minor)
		{
			if (MajorVersion < major)
				return false;
			if (MajorVersion > major)
				return true;
			return MinorVersion >= minor;
		}

		public bool HasNewTrampolineNotification {
			get { return CheckRuntimeVersion (80, 2) || CheckRuntimeVersion (81, 4); }
		}

		public bool HasAbortRuntimeInvoke {
			get { return CheckRuntimeVersion (81, 5); }
		}

		protected MonoDebuggerInfo (TargetMemoryAccess memory, TargetReader reader)
		{
			reader.Offset = 8;
			MajorVersion              = reader.ReadInteger ();
			MinorVersion              = reader.ReadInteger ();

			RuntimeFlags              = reader.ReadInteger ();

			reader.Offset = 24;

			SymbolTableSize           = reader.ReadInteger ();
			MonoTrampolineNum         = reader.ReadInteger ();
			MonoTrampolineCode        = reader.ReadAddress ();
			NotificationAddress       = reader.ReadAddress ();
			SymbolTable               = reader.ReadAddress ();
			MonoMetadataInfo          = reader.ReadAddress ();
			DebuggerVersion           = reader.ReadAddress ();

			CompileMethod             = reader.ReadAddress ();
			GetVirtualMethod          = reader.ReadAddress ();
			GetBoxedObjectMethod      = reader.ReadAddress ();
			RuntimeInvoke             = reader.ReadAddress ();
			ClassGetStaticFieldData   = reader.ReadAddress ();
			RunFinally                = reader.ReadAddress ();
			Initialize                = reader.ReadAddress ();

			CreateString              = reader.ReadAddress ();
			LookupClass               = reader.ReadAddress ();

			InsertMethodBreakpoint    = reader.ReadAddress ();
			InsertSourceBreakpoint    = reader.ReadAddress ();
			RemoveBreakpoint          = reader.ReadAddress ();

			RegisterClassInitCallback = reader.ReadAddress ();
			RemoveClassInitCallback   = reader.ReadAddress ();

			ThreadTable               = reader.ReadAddress ();

			ExecutableCodeBuffer      = reader.ReadAddress ();
			BreakpointInfo            = reader.ReadAddress ();
			BreakpointInfoIndex       = reader.ReadAddress ();

			ExecutableCodeBufferSize  = reader.ReadInteger ();
			BreakpointArraySize       = reader.ReadInteger ();

			GetMethodSignature        = reader.ReadAddress ();
			InitCodeBuffer            = reader.ReadAddress ();

			ThreadVTablePtr           = reader.ReadAddress ();
			ThreadVTable              = reader.ReadAddress ();
			EventHandlerPtr           = reader.ReadAddress ();
			EventHandler              = reader.ReadAddress ();

			UsingMonoDebugger         = reader.ReadAddress ();
			InterruptionRequest       = reader.ReadAddress ();

			if (HasAbortRuntimeInvoke)
				AbortRuntimeInvoke = reader.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, this);
		}
	}
}
