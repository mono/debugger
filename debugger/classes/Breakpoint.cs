using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;
using System.Xml;

namespace Mono.Debugger
{
	[Serializable]
	public enum HardwareWatchType {
		WatchRead,
		WatchWrite
	}

	// <summary>
	//   This is an abstract base class which is implemented by the user interface to
	//   hold the user's settings for a breakpoint.
	// </summary>
	public abstract class Breakpoint : Event
	{
		internal abstract BreakpointHandle Resolve (Thread target);

		public override void Remove (Thread target)
		{
			Deactivate (target);
		}

		// <summary>
		//   Internal breakpoint handler.
		// </summary>
		// <remarks>
		//   The return value specifies whether we already dealt with the breakpoint; so you
		//   normally make it return `true' when overriding.
		// </remarks>
		internal virtual bool BreakpointHandler (Inferior inferior, out bool remain_stopped)
		{
			remain_stopped = false;
			return false;
		}

		// <summary>
		//   This method is called each time the breakpoint is hit.
		//   It returns true if the target should remain stopped and false
		//   if the breakpoint is to be ignored.
		// </summary>
		// <remarks>
		//   The @target argument is *not* serializable and may not be used
		//   anywhere outside this handler.
		// </remarks>
		public virtual bool CheckBreakpointHit (Thread target, TargetAddress address)
		{
			return true;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), Index, Name);
		}

		protected Breakpoint (EventType type, string name, ThreadGroup group)
			: base (type, name, group)
		{ }

		protected Breakpoint (EventType type, int index, string name, ThreadGroup group)
			: base (type, index, name, group)
		{ }

		protected static EventType GetEventType (HardwareWatchType type)
		{
			switch (type) {
			case HardwareWatchType.WatchRead:
				return EventType.WatchRead;
			case HardwareWatchType.WatchWrite:
				return EventType.WatchWrite;
			default:
				throw new InternalError ();
			}
		}
	}
}
