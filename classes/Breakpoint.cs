using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	// <summary>
	//   This is an abstract base class which is implemented by the user interface to
	//   hold the user's settings for a breakpoint.
	// </summary>
	public class Breakpoint : Event
	{
		BreakpointHandle handle;
		SourceLocation location;
		TargetAddress address = TargetAddress.Null;
		int domain;

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

			if (!address.IsNull) {
				int breakpoint_id = target.InsertBreakpoint (this, address);
				handle = new SimpleBreakpointHandle (this, breakpoint_id);
			} else if (location != null)
				handle = location.InsertBreakpoint (target, this, domain);
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

		internal Breakpoint (ThreadGroup group, SourceLocation location)
			: base (location.Name, group)
		{
			this.location = location;
		}

		internal Breakpoint (string name, ThreadGroup group, TargetAddress address)
			: base (name, group)
		{
			this.address = address;
		}

		protected override void GetSessionData (SerializationInfo info)
		{
			base.GetSessionData (info);
			info.AddValue ("location", location);
		}

		protected override void SetSessionData (SerializationInfo info, Process process)
		{
			base.SetSessionData (info, process);
			location = (SourceLocation) info.GetValue ("location", typeof (SourceLocation));
		}
	}
}
