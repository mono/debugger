using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal enum NotificationType {
		InitializeManagedCode	= 1,
		AddModule,
		ReloadSymtabs,
		MethodCompiled,
		JitBreakpoint,
		InitializeThreadManager,
		AcquireGlobalThreadLock,
		ReleaseGlobalThreadLock,
		WrapperMain,
		MainExited,
		UnhandledException,
		ThreadCreated,
		ThreadAbort,
		ThreadExited,
		ThrowException,
		HandleException,
		ReachedMain,
		FinalizeManagedCode,
		ClassInitialized
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

		void Notification (Inferior inferior, NotificationType type,
				   TargetAddress data, long arg);
	}
}
