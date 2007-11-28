using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	internal class DereferencedTargetLocation : TargetLocation
	{
		TargetLocation reference;

		public DereferencedTargetLocation (TargetLocation reference)
		{
			this.reference = reference;
		}

		internal override bool HasAddress {
			get { return reference.HasAddress; }
		}

		internal override TargetAddress GetAddress (TargetMemoryAccess target)
		{
			TargetAddress address = reference.GetAddress (target);
			if (address.IsNull)
				return TargetAddress.Null;
			else
				return target.ReadAddress (address);
		}

		public override string Print ()
		{
			return String.Format ("*{0}", reference);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}", reference);
		}
	}
}
