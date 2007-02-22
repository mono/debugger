using System;
using System.Xml;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class SourceBreakpoint : Breakpoint
	{
		BreakpointHandle handle;
		DebuggerSession session;
		SourceLocation location;
		int domain;

		public override bool IsPersistent {
			get { return true; }
		}

		public SourceLocation Location {
			get { return location; }
		}

		internal SourceBreakpoint (DebuggerSession session, ThreadGroup group,
					   SourceLocation location)
			: base (EventType.Breakpoint, location.Name, group)
		{
			this.session = session;
			this.location = location;
		}

		internal SourceBreakpoint (DebuggerSession session, int index, ThreadGroup group,
					   SourceLocation location)
			: base (EventType.Breakpoint, index, location.Name, group)
		{
			this.session = session;
			this.location = location;
		}

		internal Breakpoint Clone (int breakpoint_id)
		{
			SourceBreakpoint new_bpt = new SourceBreakpoint (
				session, GetNextEventIndex (), ThreadGroup, location);
			new_bpt.handle = new SimpleBreakpointHandle (new_bpt, breakpoint_id);
			return new_bpt;
		}

		protected override void GetSessionData (XmlElement root, XmlElement element)
		{
			XmlElement location_e = root.OwnerDocument.CreateElement ("Location");
			location_e.SetAttribute ("name", location.Name);
			element.AppendChild (location_e);

			location.GetSessionData (location_e);
		}		

		internal override void Enable (Thread target)
		{
			if (handle != null)
				return;

			handle = location.InsertBreakpoint (session, target, this, domain);
		}

		internal override void Disable (Thread target)
		{
			if (handle != null) {
				handle.Remove (target);
				handle = null;
			}
		}

		internal override void OnTargetExited ()
		{
			if (location != null)
				location.OnTargetExited ();
			handle = null;
		}
	}
}
