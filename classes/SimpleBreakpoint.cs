using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public class SimpleBreakpoint : Breakpoint
	{
		public SimpleBreakpoint (string name, ThreadGroup group,
					 BreakpointCheckHandler check_handler,
					 BreakpointHitHandler hit_handler,
					 bool needs_frame, object user_data)
			: base (name, group, needs_frame)
		{
			this.check_handler = check_handler;
			this.hit_handler = hit_handler;
			this.user_data = user_data;
		}

		public SimpleBreakpoint (string name, ThreadGroup group)
			: base (name, group, true)
		{ }

		public SimpleBreakpoint (string name)
			: this (name, null)
		{ }

		BreakpointCheckHandler check_handler;
		BreakpointHitHandler hit_handler;
		object user_data;

		public override bool CheckBreakpointHit (StackFrame frame)
		{
			if (check_handler != null)
				return check_handler (frame, Index, user_data);

			return base.CheckBreakpointHit (frame);
		}

		public override void BreakpointHit (StackFrame frame)
		{
			if (hit_handler != null)
				hit_handler (frame, Index, user_data);
			else
				OnBreakpointHit (frame);
		}

		public event BreakpointEventHandler BreakpointHitEvent;

		protected virtual void OnBreakpointHit (StackFrame frame)
		{
			if (BreakpointHitEvent != null)
				BreakpointHitEvent (this, frame);
		}
	}
}
