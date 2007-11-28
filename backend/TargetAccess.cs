using System;

namespace Mono.Debugger
{
	using Mono.Debugger.Backends;

	public abstract class TargetAccess : TargetMemoryAccess
	{
		internal abstract void InsertBreakpoint (BreakpointHandle breakpoint,
							 TargetAddress address, int domain);

		internal abstract void RemoveBreakpoint (BreakpointHandle handle);
	}
}
