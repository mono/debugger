using System;
using System.Text;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	internal class RelativeTargetLocation : TargetLocation
	{
		ITargetLocation relative_to;
		TargetAddress address;

		internal RelativeTargetLocation (ITargetLocation relative_to, TargetAddress address)
			: base (0)
		{
			this.relative_to = relative_to;
			this.address = address;
			relative_to.LocationInvalid += new LocationInvalidHandler (SetInvalid);
		}

		protected override object GetHandle ()
		{
			return relative_to.Handle;
		}

		protected override TargetAddress GetAddress ()
		{
			if (!relative_to.IsValid)
				throw new LocationInvalidException ();

			return address;
		}

		protected override bool GetIsValid ()
		{
			return relative_to.IsValid;
		}

		public override object Clone ()
		{
			return new RelativeTargetLocation (relative_to, address);
		}
	}
}
