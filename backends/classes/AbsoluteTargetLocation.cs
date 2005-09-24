using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is an absolute address - usually supplied by the user.
	// </summary>
	internal class AbsoluteTargetLocation : TargetLocation
	{
		TargetAddress address;

		public AbsoluteTargetLocation (StackFrame frame, TargetAddress address)
			: base (frame)
		{
			this.address = address;
		}

		public AbsoluteTargetLocation (StackFrame frame, ITargetAccess target,
					       TargetAddress address)
			: base (frame, target)
		{
			this.address = address;
		}

		public AbsoluteTargetLocation (ITargetAccess target, TargetAddress address)
			: base (null, target)
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
