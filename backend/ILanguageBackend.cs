using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
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
		ThreadAbort,
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

	internal interface ILanguageBackend : IDisposable
	{
		string Name {
			get;
		}

		Language Language {
			get;
		}

		TargetAddress GetTrampolineAddress (TargetMemoryAccess memory,
						    TargetAddress address,
						    out bool is_start);

		MethodSource GetTrampoline (TargetMemoryAccess memory,
					    TargetAddress address);
	}
}
