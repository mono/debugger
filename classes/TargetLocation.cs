using System;
using System.Text;

namespace Mono.Debugger
{
	public class LocationInvalidException : TargetException
	{
		public LocationInvalidException ()
			: base ("Location is invalid.")
		{ }

		public LocationInvalidException (ITargetLocation location)
			: base ("Location is invalid.")
		{ }
	}

	public abstract class TargetLocation : ITargetLocation
	{
		long offset;

		protected bool is_valid = true;

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
				if (!is_valid)
					return false;

				try {
					return GetIsValid ();
				} catch {
					return false;
				}
			}
		}

		protected abstract TargetAddress GetAddress ();
		protected abstract object GetHandle ();

		protected abstract bool GetIsValid ();

		public long Offset {
			get {
				return offset;
			}
		}

		public object Handle {
			get {
				if (!IsValid)
					throw new LocationInvalidException (this);

				try {
					return GetHandle ();
				} catch {
					is_valid = false;
					throw new LocationInvalidException (this);
				}
			}
		}

		protected void SetInvalid ()
		{
			is_valid = false;
			if (LocationInvalid != null)
				LocationInvalid ();
		}

		public event LocationInvalidHandler LocationInvalid;
		
		public abstract object Clone ();
	}
}
