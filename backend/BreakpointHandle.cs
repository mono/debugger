using System;

namespace Mono.Debugger.Backends
{
	internal abstract class BreakpointHandle : DebuggerMarshalByRefObject
	{
		public readonly Breakpoint Breakpoint;

		protected BreakpointHandle (Breakpoint breakpoint)
		{
			this.Breakpoint = breakpoint;
		}

		public abstract void Remove (Thread target);
	}

	internal class SimpleBreakpointHandle : BreakpointHandle
	{
		int index;

		internal SimpleBreakpointHandle (Breakpoint breakpoint, int index)
			: base (breakpoint)
		{
			this.index = index;
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);
			index = -1;
		}
	}
}
