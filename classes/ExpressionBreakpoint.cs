using System;
using System.Xml;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class ExpressionBreakpoint : Breakpoint
	{
		public readonly DebuggerSession Session;
		public readonly LocationType LocationType;
		BreakpointHandle handle;

		public override bool IsPersistent {
			get { return true; }
		}

		public override bool IsActivated {
			get { return handle != null; }
		}

		internal override BreakpointHandle Resolve (Thread target, StackFrame frame)
		{
			if (handle != null)
				return handle;

			SourceLocation location = Session.ParseLocation (
				target, frame, LocationType, Name);
			if (location == null)
				throw new TargetException (TargetError.LocationInvalid);

			handle = location.ResolveBreakpoint (Session, this);
			return handle;
		}

		public override void Activate (Thread thread)
		{
			Resolve (thread, thread.CurrentFrame);
			if (handle == null)
				throw new TargetException (TargetError.LocationInvalid);
			handle.Insert (thread);
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
			handle = null;
		}

		protected override void GetSessionData (XmlElement root, XmlElement element)
		{
			XmlElement location_e = root.OwnerDocument.CreateElement ("Expression");
			location_e.SetAttribute ("type", LocationType.ToString ());
			location_e.SetAttribute ("expression", Name);
			element.AppendChild (location_e);
		}

		internal ExpressionBreakpoint (DebuggerSession session, int index, ThreadGroup group,
					       LocationType type, string expression)
			: base (EventType.Breakpoint, index, expression, group)
		{
			this.Session = session;
			this.LocationType = type;
		}

		internal ExpressionBreakpoint (DebuggerSession session, ThreadGroup group,
					       LocationType type, string expression)
			: base (EventType.Breakpoint, expression, group)
		{
			this.Session = session;
			this.LocationType = type;
		}
	}
}
