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

		public override bool IsActivated {
			get { return handle != null; }
		}

		internal override BreakpointHandle Resolve (TargetMemoryAccess target,
							    StackFrame frame)
		{
			if (handle != null)
				return handle;

			handle = location.ResolveBreakpoint (session, this);
			return handle;
		}

		public override void Activate (Thread target)
		{
			Resolve (target, target.CurrentFrame);
			if (handle == null)
				throw new TargetException (TargetError.LocationInvalid);
			handle.Insert (target);
		}

		public override void Deactivate (Thread target)
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
