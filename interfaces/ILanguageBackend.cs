using System;

namespace Mono.Debugger
{
	public interface ILanguageBackend
	{
		ISymbolTable SymbolTable {
			get;
		}

		// <summary>
		//   The address of the JIT's generic trampoline code.
		// </summary>
		TargetAddress GenericTrampolineCode {
			get;
		}

		TargetAddress GetTrampoline (TargetAddress address);

		// <summary>
		//   Called when a breakpoint has been hit.  Returns true if the
		//   target is still stopped.  The implementation may decide to
		//   ignore the breakpoint and continue the target - in this case,
		//   it'll return false.
		// </summary>
		bool BreakpointHit (TargetAddress address);
	}
}

