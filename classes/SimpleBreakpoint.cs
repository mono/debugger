using System;

namespace Mono.Debugger
{
	[Serializable]
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint (string name)
			: base (name, true, true)
		{ }

		public override void BreakpointHit (StackFrame frame)
		{
			OnBreakpointHit ();
		}

		public event BreakpointEventHandler BreakpointHitEvent;

		protected virtual void OnBreakpointHit ()
		{
			if (BreakpointHitEvent != null)
				BreakpointHitEvent (this);
		}
	}
}
