using System;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is an absolute address - usually supplied by the user.
	// </summary>
	internal class AbsoluteTargetLocation : TargetLocation
	{
		TargetAddress address;

		public AbsoluteTargetLocation (TargetAddress address)
		{
			this.address = address;
		}

		internal override bool HasAddress {
			get { return true; }
		}

		internal override TargetAddress GetAddress (TargetMemoryAccess target)
		{
			return address;
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
