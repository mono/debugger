using System;

namespace Mono.Debugger.Backends
{
	public interface ILanguageBackend
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

		TargetAddress GetTrampoline (IInferior inferior, TargetAddress address);

		SourceMethod GetTrampoline (TargetAddress address);
	}
}

