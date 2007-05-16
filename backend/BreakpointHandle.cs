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

		public abstract void Insert (Thread target);

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

		public override void Insert (Thread target)
		{
			throw new InternalError ();
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);
			index = -1;
		}
	}

	internal class AddressBreakpointHandle : BreakpointHandle
	{
		public readonly TargetAddress Address;
		int index = -1;

		public AddressBreakpointHandle (Breakpoint breakpoint, TargetAddress address)
			: base (breakpoint)
		{
			this.Address = address;
		}

		public override void Insert (Thread target)
		{
			index = target.InsertBreakpoint (Breakpoint, Address);
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);
			index = -1;
		}
	}
}
