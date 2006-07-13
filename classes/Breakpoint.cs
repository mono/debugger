using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;
using System.Data;

namespace Mono.Debugger
{
	[Serializable]
	public enum BreakpointType {
		Breakpoint,
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
		SourceLocation location;
		BreakpointType type;
		TargetAddress address = TargetAddress.Null;
		int domain;

		public BreakpointType Type {
			get { return type; }
		}

		public SourceLocation Location {
			get { return location; }
		}

		public override bool IsEnabled {
			get { return handle != null; }
		}

		public override void Enable (Thread target)
		{
			if (handle != null)
				return;

			switch (type) {
			case BreakpointType.Breakpoint:
				if (!address.IsNull) {
					int breakpoint_id = target.InsertBreakpoint (this, address);
					handle = new SimpleBreakpointHandle (this, breakpoint_id);
				} else if (location != null)
					handle = location.InsertBreakpoint (target, this, domain);
				break;

			case BreakpointType.WatchRead:
			case BreakpointType.WatchWrite:
				int breakpoint_id = target.InsertBreakpoint (this, address);
				handle = new SimpleBreakpointHandle (this, breakpoint_id);
				break;

			default:
				throw new InternalError ();
			}
		}

		public override void Disable (Thread target)
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
			return new Breakpoint (GetNextEventIndex (), ThreadGroup, location);
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

		internal override void GetSessionData (DataSet ds, DebuggerSession session)
		{
			DataTable location_table = ds.Tables ["Location"];
			DataTable event_table = ds.Tables ["Event"];

			int location_index = location_table.Rows.Count + 1;

			DataRow event_row = event_table.NewRow ();
			event_row ["session"] = session.Name;
			event_row ["location"] = location_index;
			GetSessionData (event_row);
			event_table.Rows.Add (event_row);

			DataRow location_row = location_table.NewRow ();
			location_row ["session"] = session.Name;
			location_row ["id"] = location_index;
			location.GetSessionData (location_row);
			location_table.Rows.Add (location_row);
		}

		internal Breakpoint (int index, ThreadGroup group, SourceLocation location)
			: base (index, location.Name, group)
		{
			this.location = location;
			this.type = BreakpointType.Breakpoint;
		}

		internal Breakpoint (ThreadGroup group, SourceLocation location)
			: base (location.Name, group)
		{
			this.location = location;
			this.type = BreakpointType.Breakpoint;
		}

		internal Breakpoint (string name, ThreadGroup group, TargetAddress address)
			: base (name, group)
		{
			this.address = address;
			this.type = BreakpointType.Breakpoint;
		}

		internal Breakpoint (TargetAddress address, BreakpointType type)
			: base (address.ToString (), ThreadGroup.Global)
		{
			this.address = address;
			this.type = type;
		}
	}
}
