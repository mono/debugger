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
		bool is_valid = true;
		bool can_revalidate;

		protected TargetLocation (long offset, bool can_revaliate)
		{
			this.offset = offset;
			this.can_revalidate = can_revalidate;
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
				return is_valid && GetIsValid ();
			}
		}

		protected abstract bool GetIsValid ();

		protected virtual void SetIsValid (bool value)
		{
			if (is_valid == value)
				return;

			is_valid = value;
			if (is_valid)
				OnLocationRevalidatedEvent ();
			else
				OnLocationInvalidEvent ();
		}

		protected abstract TargetAddress GetAddress ();
		protected abstract object GetHandle ();

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

		public bool CanRevalidate {
			get {
				return can_revalidate;
			}
		}

		public event LocationEventHandler LocationInvalidEvent;
		public event LocationEventHandler LocationRevalidatedEvent;

		protected virtual void OnLocationInvalidEvent ()
		{
			if (LocationInvalidEvent != null)
				LocationInvalidEvent (this);
		}

		protected virtual void OnLocationRevalidatedEvent ()
		{
			if (LocationRevalidatedEvent != null)
				LocationRevalidatedEvent (this);
		}

		public abstract object Clone ();
	}
}
