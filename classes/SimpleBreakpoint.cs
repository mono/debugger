using System;
using System.Runtime.Serialization;

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

		//
		// ISerializable
		//

		public override void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData (info, context);
		}

		protected SimpleBreakpoint (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{ }
	}
}
