using System;

namespace Mono.Debugger
{
	// <summary>
	//   This is an absolute address - usually supplied by the user.
	// </summary>
	public class AbsoluteTargetLocation : TargetLocation
	{
		TargetAddress address;

		public AbsoluteTargetLocation (StackFrame frame, TargetAddress address)
			: base (frame, false, 0)
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
			return new AbsoluteTargetLocation (frame, address + offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}", address);
		}
	}
}
