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
		ThrowException,
		HandleException,
		ReachedMain
	}

	internal interface ILanguageBackend : IDisposable
	{
		string Name {
			get;
		}

		Language Language {
			get;
		}

		TargetAddress RuntimeInvokeFunc {
			get;
		}

		TargetAddress CompileMethodFunc {
			get;
		}

		TargetAddress GetVirtualMethodFunc {
			get;
		}

		TargetAddress GetBoxedObjectFunc {
			get;
		}

		TargetAddress GetTrampolineAddress (ITargetMemoryAccess memory,
						    TargetAddress address,
						    out bool is_start);

		SourceMethod GetTrampoline (ITargetMemoryAccess memory,
					    TargetAddress address);

		void Notification (Inferior inferior, NotificationType type,
				   TargetAddress data, long arg);
	}
}
