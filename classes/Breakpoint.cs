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
	public class Breakpoint : Event
	{
		BreakpointHandle handle;
		DebuggerSession session;
		SourceLocation location;
		TargetAddress address = TargetAddress.Null;
		int domain;

		public SourceLocation Location {
			get { return location; }
		}

		internal override void Enable (Thread target)
		{
			if (handle != null)
				return;

			switch (Type) {
			case EventType.Breakpoint:
				if (!address.IsNull) {
					int breakpoint_id = target.InsertBreakpoint (this, address);
					handle = new SimpleBreakpointHandle (this, breakpoint_id);
				} else if (location != null)
					handle = location.InsertBreakpoint (session, target, this, domain);
				else
					throw new TargetException (TargetError.LocationInvalid);
				break;

			case EventType.WatchRead:
			case EventType.WatchWrite:
				int breakpoint_id = target.InsertBreakpoint (this, address);
				handle = new SimpleBreakpointHandle (this, breakpoint_id);
				break;

			default:
				throw new InternalError ();
			}
		}

		internal override void Disable (Thread target)
		{
			if (handle != null) {
				handle.Remove (target);
				handle = null;
			}
		}

		public override void Remove (Thread target)
		{
			Disable (target);
		}

		internal override void OnTargetExited ()
		{
			if (location != null)
				location.OnTargetExited ();
			handle = null;
		}

		internal Breakpoint Clone (int breakpoint_id)
		{
			Breakpoint new_bpt = Clone ();
			new_bpt.handle = new SimpleBreakpointHandle (new_bpt, breakpoint_id);
			return new_bpt;
		}

		protected virtual Breakpoint Clone ()
		{
			return new Breakpoint (session, GetNextEventIndex (), ThreadGroup, location);
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

		protected override void GetSessionData (XmlElement root, XmlElement element)
		{
			XmlElement location_e = root.OwnerDocument.CreateElement ("Location");
			location_e.SetAttribute ("name", location.Name);
			element.AppendChild (location_e);

			location.GetSessionData (location_e);
		}		

		internal Breakpoint (DebuggerSession session, int index, ThreadGroup group,
				     SourceLocation location)
			: base (EventType.Breakpoint, index, location.Name, group)
		{
			this.session = session;
			this.location = location;
		}

		internal Breakpoint (DebuggerSession session, ThreadGroup group,
				     SourceLocation location)
			: base (EventType.Breakpoint, location.Name, group)
		{
			this.session = session;
			this.location = location;
		}

		internal Breakpoint (string name, ThreadGroup group, TargetAddress address)
			: base (EventType.Breakpoint, name, group)
		{
			this.address = address;
		}

		internal Breakpoint (HardwareWatchType type, TargetAddress address)
			: base (GetEventType (type), address.ToString (), ThreadGroup.Global)
		{
			this.address = address;
		}

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
