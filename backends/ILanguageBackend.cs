using System;

namespace Mono.Debugger.Backends
{
	internal enum NotificationType {
		InitializeManagedCode	= 1,
		ReloadSymtabs,
		MethodCompiled,
		JitBreakpoint,
		InitializeThreadManager,
		AcquireGlobalThreadLock,
		ReleaseGlobalThreadLock,
		WrapperMain,
		MainExited
	}

	internal interface ILanguageBackend
	{
		string Name {
			get;
		}

		// <summary>
		//   The address of the JIT's generic trampoline code.
		// </summary>
		TargetAddress GenericTrampolineCode {
			get;
		}

		TargetAddress RuntimeInvokeFunc {
			get;
		}

		TargetAddress CompileMethodFunc {
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
