using System;
using System.Text;

namespace Mono.Debugger
{
	public class LocationInvalidException : TargetException
	{
		public LocationInvalidException (ITargetLocation location)
			: base ("Location is invalid.")
		{ }
	}

	public abstract class TargetLocation : ITargetLocation
	{
		long offset;

		protected bool is_valid;

		protected TargetLocation (long offset)
		{
			this.offset = offset;
		}

		public TargetAddress Address {
			get {
				if (!IsValid)
					throw new LocationInvalidException (this);

				try {
					return GetAddress ();
				} catch {
					is_valid = false;
					throw new LocationInvalidException (this);
				}
			}
		}

		public bool IsValid {
			get {
				try {
					return ReValidate ();
				} catch {
					return false;
				}
			}
		}

		public abstract TargetAddress GetAddress ();

		public abstract bool ReValidate ();

		public long Offset {
			get {
				return offset;
			}
		}

		public abstract object Clone ();
	}
}
