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
		long offset;

		public RelativeTargetLocation (TargetLocation relative_to, long offset)
			: base (relative_to.StackFrame, relative_to.TargetAccess, false)
		{
			this.relative_to = relative_to;
			this.offset = offset;
		}

		public override bool HasAddress {
			get { return true; }
		}

		protected override TargetAddress GetAddress ()
		{
			return relative_to.Address + offset;
		}

		protected override TargetLocation Clone ()
		{
			return new RelativeTargetLocation (relative_to, offset);
		}

		public override string Print ()
		{
			TargetAddress address = relative_to.Address;
			if (offset > 0)
				return String.Format ("{0}+{1}", address, offset);
			else
				return String.Format ("{0}-{1}", address, -offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", relative_to, offset);
		}
	}
}
