using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint (string name)
			: base (name, true)
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
