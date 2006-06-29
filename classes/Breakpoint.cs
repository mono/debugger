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

		internal Breakpoint Clone (int breakpoint_id)
		{
			Breakpoint new_bpt = Clone ();
			new_bpt.handle = new SimpleBreakpointHandle (new_bpt, breakpoint_id);
			return new_bpt;
		}

		protected virtual Breakpoint Clone ()
		{
			return new Breakpoint (ThreadGroup, location);
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

		internal override void GetSessionData (DataRow row)
		{
			DataTable location_table = row.Table.DataSet.Tables ["Location"];
			DataRow location_row = location_table.NewRow ();
			int location_index = location_table.Rows.Count + 1;
			location_row ["id"] = location_index;
			location.GetSessionData (location_row);
			location_table.Rows.Add (location_row);

			row ["location"] = location_index;
			base.GetSessionData (row);
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
