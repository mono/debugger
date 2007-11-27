using System;
using System.Xml;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class AddressBreakpoint : Breakpoint
	{
		AddressBreakpointHandle handle;
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

		internal override BreakpointHandle Resolve (TargetMemoryAccess target,
							    StackFrame frame)
		{
			return DoResolve ();
		}

		private BreakpointHandle DoResolve ()
		{
			if (handle != null)
				return handle;

			switch (Type) {
			case EventType.Breakpoint:
				handle = new AddressBreakpointHandle (this, address);
				break;

			case EventType.WatchRead:
			case EventType.WatchWrite:
				handle = new AddressBreakpointHandle (this, address);
				break;

			default:
				throw new InternalError ();
			}

			return handle;
		}

		internal void Insert (Inferior inferior)
		{
			if (handle == null)
				handle = new AddressBreakpointHandle (this, address);

			handle.Insert (inferior);
		}

		internal void Remove (Inferior inferior)
		{
			if (handle != null) {
				handle.Remove (inferior);
				handle = null;
			}
		}

		public override void Activate (Thread thread)
		{
			DoResolve ();
			if (handle == null)
				throw new TargetException (TargetError.LocationInvalid);
			handle.Insert (thread);
		}

		public override void Deactivate (Thread thread)
		{
			if (handle != null) {
				handle.Remove (thread);
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
