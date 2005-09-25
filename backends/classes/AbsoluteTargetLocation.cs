using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is an absolute address - usually supplied by the user.
	// </summary>
	internal class AbsoluteTargetLocation : TargetLocation
	{
		TargetAddress address;

		public AbsoluteTargetLocation (TargetAccess target, TargetAddress address)
			: base (target)
		{
			this.address = address;
		}

		public override bool HasAddress {
			get { return true; }
		}

		public override TargetAddress Address {
			get { return address; }
		}

		public override string Print ()
		{
			return address.ToString ();
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}", address);
		}
	}
}
