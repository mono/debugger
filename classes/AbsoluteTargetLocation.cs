using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is an absolute address - usually supplied by the user.
	// </summary>
	[Serializable]
	public class AbsoluteTargetLocation : TargetLocation
	{
		TargetAddress address;

		public AbsoluteTargetLocation (StackFrame frame, TargetAddress address)
			: base (frame, false, 0)
		{
			this.address = address;
		}

		public AbsoluteTargetLocation (StackFrame frame, ITargetAccess target,
					       TargetAddress address)
			: base (frame, target, false, 0)
		{
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
			return new AbsoluteTargetLocation (frame, target, address + offset);
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
