using System;

namespace Mono.Debugger
{
	[Serializable]
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint ()
			: base ()
		{ }

		public override bool BreakpointHit (StackFrame frame)
		{
			Console.WriteLine ("BREAKPOINT HIT: {0} {1}", this, frame);
			OnBreakpointHit ();
			return true;
		}

		public event BreakpointEventHandler BreakpointHitEvent;

		protected virtual void OnBreakpointHit ()
		{
			if (BreakpointHitEvent != null)
				BreakpointHitEvent (this);
		}
	}
}
