using System;
using System.Xml;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public enum LocationType
	{
		Default,
		Method,
		Constructor,
		DelegateInvoke,
		PropertyGetter,
		PropertySetter,
		EventAdd,
		EventRemove
	}

	public interface ILocationParser
	{
		SourceLocation Parse (Thread target, LocationType type, string name);
	}

	public class ExpressionBreakpoint : Breakpoint
	{
		public readonly DebuggerSession Session;
		public readonly LocationType LocationType;
		BreakpointHandle handle;
		int domain;

		public override bool IsPersistent {
			get { return true; }
		}

		public override bool IsActivated {
			get { return handle != null; }
		}

		public override void Activate (Thread target)
		{
			if (handle != null)
				return;

			SourceLocation location = Session.ParseLocation (target, LocationType, Name);
			if (location == null)
				throw new TargetException (TargetError.LocationInvalid);

			handle = location.InsertBreakpoint (Session, target, this, domain);
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

		internal Breakpoint Clone (int breakpoint_id)
		{
			ExpressionBreakpoint new_bpt = new ExpressionBreakpoint (
				Session, GetNextEventIndex (), ThreadGroup, LocationType, Name);
			new_bpt.handle = new SimpleBreakpointHandle (new_bpt, breakpoint_id);
			return new_bpt;
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
