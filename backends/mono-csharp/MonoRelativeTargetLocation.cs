using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	// <summary>
	//   This is just an address, but its lifetime is tied to the lifetime of another location.
	// </summary>
	internal class MonoRelativeTargetLocation : MonoTargetLocation
	{
		MonoTargetLocation relative_to;
		TargetAddress address;

		internal MonoRelativeTargetLocation (MonoTargetLocation relative_to, TargetAddress address)
			: base (relative_to, address, false)
		{
			this.relative_to = relative_to;
			this.address = address;

			relative_to.LocationInvalidEvent += new LocationEventHandler (location_invalid);
		}

		void location_invalid (MonoTargetLocation location)
		{
			SetInvalid ();
		}

		public override bool HasAddress {
			get {
				return true;
			}
		}

		protected override TargetAddress GetAddress ()
		{
			return address;
		}

		protected override MonoTargetLocation Clone (int offset)
		{
			return new MonoRelativeTargetLocation (relative_to, address + offset);
		}
	}
}
