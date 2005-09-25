using System;

namespace Mono.Debugger
{
	public delegate bool BreakpointCheckHandler (Breakpoint bpt, TargetAccess target,
						     TargetAddress address);

	[Serializable]
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint (string name, ThreadGroup group,
					 BreakpointCheckHandler check_handler)
			: base (name, group)
		{
			this.check_handler = check_handler;
		}

		public SimpleBreakpoint (string name, ThreadGroup group)
			: base (name, group)
		{ }

		public SimpleBreakpoint (string name)
			: this (name, ThreadGroup.Global)
		{ }

		BreakpointCheckHandler check_handler;

		public override bool CheckBreakpointHit (TargetAccess target, TargetAddress address)
		{
			if (check_handler != null)
				return check_handler (this, target, address);

			return base.CheckBreakpointHit (target, address);
		}
	}
}
