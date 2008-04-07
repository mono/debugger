using System;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is just an address, but its lifetime is tied to the lifetime of another location.
	// </summary>
	internal class RelativeTargetLocation : TargetLocation
	{
		TargetLocation relative_to;
		long offset;

		public RelativeTargetLocation (TargetLocation relative_to, long offset)
		{
			this.relative_to = relative_to;
			this.offset = offset;
		}

		internal override bool HasAddress {
			get { return relative_to.HasAddress; }
		}

		internal override TargetAddress GetAddress (TargetMemoryAccess target)
		{
			return relative_to.GetAddress (target) + offset;
		}

		public override string Print ()
		{
			if (offset > 0)
				return String.Format ("[{0}]+0x{1:x}", relative_to.Print (), offset);
			else
				return String.Format ("[{0}]-0x{1:x}", relative_to.Print (), -offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", relative_to, offset);
		}
	}
}
