using System;
using System.Xml;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class AddressBreakpoint : Breakpoint
	{
		BreakpointHandle handle;
		TargetAddress address = TargetAddress.Null;
		int domain;

		public override bool IsPersistent {
			get { return false; }
		}

		public TargetAddress Address {
			get { return address; }
		}

		internal AddressBreakpoint (string name, ThreadGroup group, TargetAddress address)
			: base (EventType.Breakpoint, name, group)
		{
			this.address = address;
		}

		internal AddressBreakpoint (HardwareWatchType type, TargetAddress address)
			: base (GetEventType (type), address.ToString (), ThreadGroup.Global)
		{
			this.address = address;
		}

		public override bool IsActivated {
			get { return handle != null; }
		}

		public override void Activate (Thread target)
		{
			if (handle != null)
				return;

			int breakpoint_id;
			switch (Type) {
			case EventType.Breakpoint:
				breakpoint_id = target.InsertBreakpoint (this, address);
				handle = new SimpleBreakpointHandle (this, breakpoint_id);
				break;

			case EventType.WatchRead:
			case EventType.WatchWrite:
				breakpoint_id = target.InsertBreakpoint (this, address);
				handle = new SimpleBreakpointHandle (this, breakpoint_id);
				break;

			default:
				throw new InternalError ();
			}
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
			throw new InvalidOperationException ();
		}
	}
}
