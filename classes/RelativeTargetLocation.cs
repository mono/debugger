using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is just an address, but its lifetime is tied to the lifetime of another location.
	// </summary>
	[Serializable]
	public class RelativeTargetLocation : TargetLocation
	{
		TargetLocation relative_to;
		TargetAddress address;

		public RelativeTargetLocation (TargetLocation relative_to, TargetAddress address)
			: base (relative_to.StackFrame, relative_to.TargetAccess, false, 0)
		{
			this.relative_to = relative_to;
			this.address = address;
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

		public override string Print ()
		{
			return address.ToString ();
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", relative_to, address);
		}
	}
}
