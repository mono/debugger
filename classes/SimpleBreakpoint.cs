using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public delegate bool BreakpointCheckHandler (Breakpoint bpt, Thread target,
						     TargetAddress address);

	[Serializable]
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint (ThreadGroup group, SourceLocation location,
					 BreakpointCheckHandler check_handler)
			: base (group, location)
		{
			this.check_handler = check_handler;
		}

		public SimpleBreakpoint (ThreadGroup group, SourceLocation location)
			: base (group, location)
		{ }

		BreakpointCheckHandler check_handler;

		public override bool CheckBreakpointHit (Thread target, TargetAddress address)
		{
			if (check_handler != null)
				return check_handler (this, target, address);

			return true;
		}

		public override Breakpoint Clone ()
		{
			return new SimpleBreakpoint (ThreadGroup, Location, check_handler);
		}
	}
}
