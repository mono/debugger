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
using Mono.Debugger.Languages.Mono;


namespace Mono.Debugger.Backends
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
		DomainUnload
	}

	internal class MonoThreadManager
	{
		ThreadManager thread_manager;
		MonoDebuggerInfo debugger_info;
		Inferior inferior;

		public static MonoThreadManager Initialize (ThreadManager thread_manager,
							    Inferior inferior, bool attach)
		{
			TargetAddress info = inferior.GetSectionAddress (".mdb_debug_info");
			if (!info.IsNull)
				info = inferior.ReadAddress (info);
			else
				info = inferior.SimpleLookup ("MONO_DEBUGGER__debugger_info");
			if (info.IsNull)
				return null;

			return new MonoThreadManager (thread_manager, inferior, info, attach);
		}

		protected MonoThreadManager (ThreadManager thread_manager, Inferior inferior,
					     TargetAddress info, bool attach)
		{
			this.inferior = inferior;
			this.thread_manager = thread_manager;

			debugger_info = MonoDebuggerInfo.Create (inferior, info);

			notification_bpt = new InitializeBreakpoint (this, debugger_info.Initialize);
			notification_bpt.Insert (inferior);
		}

		AddressBreakpoint notification_bpt;
		IntPtr mono_runtime_info;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize_mono_runtime (
			long notification_address, long executable_code_buffer,
			int executable_code_buffer_size, long breakpoint_table,
			int breakpoint_table_size);

		protected void initialize_notifications (Inferior inferior)
		{
			TargetAddress notification_address = inferior.ReadAddress (
				debugger_info.NotificationAddress);
			TargetAddress executable_code_buffer = inferior.ReadAddress (
				debugger_info.ExecutableCodeBuffer);

			mono_runtime_info = mono_debugger_server_initialize_mono_runtime (
				notification_address.Address, executable_code_buffer.Address,
				debugger_info.ExecutableCodeBufferSize,
				debugger_info.BreakpointTable.Address,
				debugger_info.BreakpointTableSize);
			inferior.SetRuntimeInfo (mono_runtime_info);

			inferior.WriteInteger (debugger_info.DebuggerVersion, 2);

			if (notification_bpt != null) {
				notification_bpt.Remove (inferior);
				notification_bpt = null;
			}
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

		TargetAddress main_function;
		TargetAddress main_thread;
		MonoLanguageBackend csharp_language;

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get { return debugger_info; }
		}

		internal MonoMetadataInfo MonoMetadataInfo {
			get { return debugger_info.MonoMetadataInfo; }
		}

		int index;
		internal void ThreadCreated (SingleSteppingEngine sse)
		{
			sse.Inferior.SetRuntimeInfo (mono_runtime_info);

			if (++index < 3)
				sse.SetDaemon ();
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
					engine.SetLMFAddress (lmf);

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

					engine = engine.ProcessServant.GetEngineByTID (tid);

					if (engine == null)
						throw new InternalError ();

					engine.OnManagedThreadCreated (data);
					break;
				}

				case NotificationType.GcThreadExited:
					engine.OnManagedThreadExited ();
					break;

				case NotificationType.InitializeThreadManager:
					csharp_language = inferior.Process.CreateMonoLanguage (
						debugger_info);
					if (engine.ProcessServant.IsAttached)
						csharp_language.InitializeAttach (inferior);
					else
						csharp_language.Initialize (inferior);

					inferior.InitializeModules ();
					if (!engine.ProcessServant.IsAttached)
						engine.ProcessServant.InitializeThreads (inferior);

					break;

				case NotificationType.WrapperMain:
				case NotificationType.MainExited:
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
					csharp_language = null;
					break;

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
		public const int  MinDynamicVersion = 63;
		public const int  MaxDynamicVersion = 63;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		public readonly TargetAddress NotificationAddress;
		public readonly TargetAddress MonoTrampolineCode;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress GetVirtualMethod;
		public readonly TargetAddress GetBoxedObjectMethod;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupClass;
		public readonly TargetAddress RunFinally;
		public readonly TargetAddress InsertBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RuntimeClassInit;
		public readonly TargetAddress Attach;
		public readonly TargetAddress Detach;
		public readonly TargetAddress Initialize;
		public readonly TargetAddress GetLMFAddress;
		public readonly TargetAddress DoTrampoline;
		public readonly TargetAddress DebuggerVersion;
		public readonly TargetAddress ThreadTable;
		public readonly TargetAddress ExecutableCodeBuffer;
		public readonly int ExecutableCodeBufferSize;
		public readonly TargetAddress BreakpointTable;
		public readonly int BreakpointTableSize;

		public readonly MonoMetadataInfo MonoMetadataInfo;

		public static MonoDebuggerInfo Create (TargetMemoryAccess memory, TargetAddress info)
		{
			TargetBinaryReader header = memory.ReadMemory (info, 16).GetReader ();
			long magic = header.ReadInt64 ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInt32 ();
			if (version < MinDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at least {1}.", version,
					MonoDebuggerInfo.MinDynamicVersion);
			if (version > MaxDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at most {1}.", version,
					MonoDebuggerInfo.MaxDynamicVersion);

			int size = header.ReadInt32 ();

			TargetReader reader = new TargetReader (memory.ReadMemory (info, size));
			return new MonoDebuggerInfo (memory, reader);
		}

		protected MonoDebuggerInfo (TargetMemoryAccess memory, TargetReader reader)
		{
			/* skip past magic, version, and total_size */
			reader.Offset = 16;

			SymbolTableSize           = reader.ReadInteger ();

			reader.Offset = 24;
			NotificationAddress       = reader.ReadAddress ();
			MonoTrampolineCode        = reader.ReadAddress ();
			SymbolTable               = reader.ReadAddress ();
			TargetAddress metadata_info = reader.ReadAddress ();
			CompileMethod             = reader.ReadAddress ();
			GetVirtualMethod          = reader.ReadAddress ();
			GetBoxedObjectMethod      = reader.ReadAddress ();
			RuntimeInvoke             = reader.ReadAddress ();
			ClassGetStaticFieldData   = reader.ReadAddress ();
			RunFinally                = reader.ReadAddress ();
			Attach                    = reader.ReadAddress ();
			Detach                    = reader.ReadAddress ();
			Initialize                = reader.ReadAddress ();
			GetLMFAddress             = reader.ReadAddress ();

			CreateString              = reader.ReadAddress ();
			LookupClass               = reader.ReadAddress ();
			InsertBreakpoint          = reader.ReadAddress ();
			RemoveBreakpoint          = reader.ReadAddress ();
			RuntimeClassInit          = reader.ReadAddress ();

			DebuggerVersion           = reader.ReadAddress ();
			ThreadTable               = reader.ReadAddress ();

			DoTrampoline              = reader.ReadAddress ();

			ExecutableCodeBuffer      = reader.ReadAddress ();
			BreakpointTable           = reader.ReadAddress ();

			ExecutableCodeBufferSize  = reader.ReadInteger ();
			BreakpointTableSize       = reader.ReadInteger ();

			MonoMetadataInfo = new MonoMetadataInfo (memory, metadata_info);

			Report.Debug (DebugFlags.JitSymtab, this);
		}
	}

	internal class MonoMetadataInfo
	{
		public readonly int MonoDefaultsSize;
		public readonly TargetAddress MonoDefaultsAddress;
		public readonly int TypeSize;
		public readonly int ArrayTypeSize;
		public readonly int KlassSize;
		public readonly int ThreadSize;

		public readonly int ThreadTidOffset;
		public readonly int ThreadStackPtrOffset;
		public readonly int ThreadEndStackOffset;

		public readonly int KlassImageOffset;
		public readonly int KlassInstanceSizeOffset;
		public readonly int KlassParentOffset;
		public readonly int KlassTokenOffset;
		public readonly int KlassFieldOffset;
		public readonly int KlassFieldCountOffset;
		public readonly int KlassMethodsOffset;
		public readonly int KlassMethodCountOffset;
		public readonly int KlassThisArgOffset;
		public readonly int KlassByValArgOffset;
		public readonly int KlassGenericClassOffset;
		public readonly int KlassGenericContainerOffset;
		public readonly int KlassVTableOffset;
		public readonly int FieldInfoSize;
		public readonly int FieldInfoTypeOffset;
		public readonly int FieldInfoOffsetOffset;

		public readonly int MonoDefaultsCorlibOffset;
		public readonly int MonoDefaultsObjectOffset;
		public readonly int MonoDefaultsByteOffset;
		public readonly int MonoDefaultsVoidOffset;
		public readonly int MonoDefaultsBooleanOffset;
		public readonly int MonoDefaultsSByteOffset;
		public readonly int MonoDefaultsInt16Offset;
		public readonly int MonoDefaultsUInt16Offset;
		public readonly int MonoDefaultsInt32Offset;
		public readonly int MonoDefaultsUInt32Offset;
		public readonly int MonoDefaultsIntOffset;
		public readonly int MonoDefaultsUIntOffset;
		public readonly int MonoDefaultsInt64Offset;
		public readonly int MonoDefaultsUInt64Offset;
		public readonly int MonoDefaultsSingleOffset;
		public readonly int MonoDefaultsDoubleOffset;
		public readonly int MonoDefaultsCharOffset;
		public readonly int MonoDefaultsStringOffset;
		public readonly int MonoDefaultsEnumOffset;
		public readonly int MonoDefaultsArrayOffset;
		public readonly int MonoDefaultsDelegateOffset;
		public readonly int MonoDefaultsExceptionOffset;

		public readonly int MonoMethodKlassOffset;
		public readonly int MonoMethodTokenOffset;
		public readonly int MonoMethodFlagsOffset;
		public readonly int MonoMethodInflatedOffset;

		public readonly int MonoVTableKlassOffset;
		public readonly int MonoVTableVTableOffset;

		public MonoMetadataInfo (TargetMemoryAccess memory, TargetAddress address)
		{
			int size = memory.ReadInteger (address);
			TargetBinaryReader reader = memory.ReadMemory (address, size).GetReader ();
			reader.ReadInt32 ();

			MonoDefaultsSize = reader.ReadInt32 ();
			MonoDefaultsAddress = new TargetAddress (
				memory.TargetInfo.AddressDomain, reader.ReadAddress ());

			TypeSize = reader.ReadInt32 ();
			ArrayTypeSize = reader.ReadInt32 ();
			KlassSize = reader.ReadInt32 ();
			ThreadSize = reader.ReadInt32 ();

			ThreadTidOffset = reader.ReadInt32 ();
			ThreadStackPtrOffset = reader.ReadInt32 ();
			ThreadEndStackOffset = reader.ReadInt32 ();

			KlassImageOffset = reader.ReadInt32 ();
			KlassInstanceSizeOffset = reader.ReadInt32 ();
			KlassParentOffset = reader.ReadInt32 ();
			KlassTokenOffset = reader.ReadInt32 ();
			KlassFieldOffset = reader.ReadInt32 ();
			KlassMethodsOffset = reader.ReadInt32 ();
			KlassMethodCountOffset = reader.ReadInt32 ();
			KlassThisArgOffset = reader.ReadInt32 ();
			KlassByValArgOffset = reader.ReadInt32 ();
			KlassGenericClassOffset = reader.ReadInt32 ();
			KlassGenericContainerOffset = reader.ReadInt32 ();
			KlassVTableOffset = reader.ReadInt32 ();

			FieldInfoSize = reader.ReadInt32 ();
			FieldInfoTypeOffset = reader.ReadInt32 ();
			FieldInfoOffsetOffset = reader.ReadInt32 ();

			KlassFieldCountOffset = KlassMethodCountOffset - 8;

			MonoDefaultsCorlibOffset = reader.ReadInt32 ();
			MonoDefaultsObjectOffset = reader.ReadInt32 ();
			MonoDefaultsByteOffset = reader.ReadInt32 ();
			MonoDefaultsVoidOffset = reader.ReadInt32 ();
			MonoDefaultsBooleanOffset = reader.ReadInt32 ();
			MonoDefaultsSByteOffset = reader.ReadInt32 ();
			MonoDefaultsInt16Offset = reader.ReadInt32 ();
			MonoDefaultsUInt16Offset = reader.ReadInt32 ();
			MonoDefaultsInt32Offset = reader.ReadInt32 ();
			MonoDefaultsUInt32Offset = reader.ReadInt32 ();
			MonoDefaultsIntOffset = reader.ReadInt32 ();
			MonoDefaultsUIntOffset = reader.ReadInt32 ();
			MonoDefaultsInt64Offset = reader.ReadInt32 ();
			MonoDefaultsUInt64Offset = reader.ReadInt32 ();
			MonoDefaultsSingleOffset = reader.ReadInt32 ();
			MonoDefaultsDoubleOffset = reader.ReadInt32 ();
			MonoDefaultsCharOffset = reader.ReadInt32 ();
			MonoDefaultsStringOffset = reader.ReadInt32 ();
			MonoDefaultsEnumOffset = reader.ReadInt32 ();
			MonoDefaultsArrayOffset = reader.ReadInt32 ();
			MonoDefaultsDelegateOffset = reader.ReadInt32 ();
			MonoDefaultsExceptionOffset = reader.ReadInt32 ();

			MonoMethodKlassOffset = reader.ReadInt32 ();
			MonoMethodTokenOffset = reader.ReadInt32 ();
			MonoMethodFlagsOffset = reader.ReadInt32 ();
			MonoMethodInflatedOffset = reader.ReadInt32 ();

			MonoVTableKlassOffset = reader.ReadInt32 ();
			MonoVTableVTableOffset = reader.ReadInt32 ();
		}
	}
}
