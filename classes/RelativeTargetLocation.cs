using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is just an address, but its lifetime is tied to the lifetime of another location.
	// </summary>
	public class RelativeTargetLocation : TargetLocation
	{
		TargetLocation relative_to;
		TargetAddress address;

		public RelativeTargetLocation (TargetLocation relative_to, TargetAddress address)
			: base (relative_to.StackFrame, false, 0)
		{
			this.relative_to = relative_to;
			this.address = address;

			relative_to.LocationInvalidEvent += new LocationEventHandler (location_invalid);
		}

		void location_invalid (TargetLocation location)
		{
			SetInvalid ();
		}

		public override bool HasAddress {
			get { return true; }
		}

		protected override TargetAddress GetAddress ()
		{
			return address;
		}

		protected override TargetLocation Clone (long offset)
		{
			return new RelativeTargetLocation (relative_to, address + offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", relative_to, address);
		}
	}
}
