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

		// <summary>
		//   Called when a breakpoint has been hit.  Returns true if the
		//   should remain stopped or false if the breakpoint should be
		//   ignored.
		// </summary>
		// <remarks>
		//   The implementation must not continue the target itself, this
		//   is done automatically by the SingleSteppingEngine.
		// </remarks>
		bool BreakpointHit (IInferior inferior, TargetAddress address);
	}
}

