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
			: base (0, relative_to.CanRevalidate)
		{
			this.relative_to = relative_to;
			this.address = address;
			relative_to.LocationInvalidEvent += new LocationEventHandler (location_invalid);
			relative_to.LocationRevalidatedEvent += new LocationEventHandler (location_revalidated);
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

		void location_invalid (ITargetLocation location)
		{
			SetIsValid (false);
		}

		void location_revalidated (ITargetLocation location)
		{
			SetIsValid (true);
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
