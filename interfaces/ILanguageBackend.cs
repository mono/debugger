using System;

namespace Mono.Debugger
{
	public delegate void ModulesChangedHandler ();

	public interface ILanguageBackend
	{
		string Name {
			get;
		}

		ISymbolTable SymbolTable {
			get;
		}

		Module[] Modules {
			get;
		}

		event ModulesChangedHandler ModulesChangedEvent;

		// <summary>
		//   The address of the JIT's generic trampoline code.
		// </summary>
		TargetAddress GenericTrampolineCode {
			get;
		}

		TargetAddress GetTrampoline (TargetAddress address);

		// <summary>
		//   Called when a breakpoint has been hit.  Returns true if the
		//   should remain stopped or false if the breakpoint should be
		//   ignored.
		// </summary>
		// <remarks>
		//   The implementation must not continue the target itself, this
		//   is done automatically by the SingleSteppingEngine.
		// </remarks>
		bool BreakpointHit (TargetAddress address);
	}
}

