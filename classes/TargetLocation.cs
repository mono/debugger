using System;

namespace Mono.Debugger.Languages
{
	public delegate void LocationEventHandler (TargetLocation location);

	// <summary>
	//   This class represents the `location' of a variable.  The idea is that we do
	//   not always have an address for a variable (for instance if it's stored in a
	//   register) and that an addresses lifetime may be limited.
	// </summary>
	public abstract class TargetLocation : ICloneable
	{
		protected StackFrame frame;
		protected long offset;
		protected bool is_byref;
		bool is_valid;

		protected TargetLocation (StackFrame frame, bool is_byref, long offset)
		{
			this.is_byref = is_byref;
			this.offset = offset;
			this.frame = frame;
			this.is_valid = true;

			frame.FrameInvalidEvent += new ObjectInvalidHandler (frame_invalid);
		}

		// <summary>
		//   The stack frame this location belongs to.
		// </summary>
		public StackFrame StackFrame {
			get { return frame; }
		}

		// <summary>
		//   If this variable is a reference type.  The actual contents of a
		//   reference type starts at the dereferenced address plus `Offset'.
		// </summary>
		public bool IsByRef {
			get { return is_byref; }
		}

		// <summary>
		//   After the variable's address is computed and - if it's a reference
		//   type - dereferenced, this offset is added to it.
		//
		//   If this is a register variable, this offset is added to the value in
		//   the register.
		// </summary>
		public long Offset {
			get { return offset; }
		}

		// <summary>
		//   Whether this variable has an address.  A variable may not have an
		//   address, for instance if it's stored in a register.
		// </summary>
		public abstract bool HasAddress {
			get;
		}

		// <summary>
		//   If the variable has an address (HasAddress must be true), compute the
		//   address of its actual contents.
		// </summary>
		public TargetAddress Address {
			get {
				if (!HasAddress)
					throw new InvalidOperationException ();
				if (!IsValid)
					return TargetAddress.Null;

				// First get the address of this variable.
				TargetAddress address;
				try {
					address = GetAddress ();
				} catch (TargetException ex) {
					SetInvalid ();
					return TargetAddress.Null;
				}

				if (address.IsNull) {
					SetInvalid ();
					return TargetAddress.Null;
				}

				// If the type is a reference type, the pointer on the
				// stack has already been dereferenced, so address now
				// points to the actual data.
				return address + Offset;
			}
		}

		public TargetAddress GlobalAddress {
			get {
				TargetAddress address = Address;
				if (address.IsNull)
					return TargetAddress.Null;

				return new TargetAddress (
					TargetAccess.GlobalAddressDomain, address.Address);
			}
		}

		// <summary>
		//   Whether this location is currently valid.  A location becomes invalid
		//   if the variable's lifetime has expired.  This usually happens the
		//   next time the target is resumed and leaves the variable's scope.
		// </summary>
		public bool IsValid {
			get { return is_valid; }
		}

		void frame_invalid (object o)
		{
			SetInvalid ();
		}

		protected void SetInvalid ()
		{
			if (is_valid) {
				is_valid = false;
				OnLocationInvalidEvent ();
			}
		}

		protected abstract TargetAddress GetAddress ();

		public virtual ITargetMemoryReader ReadMemory (int size)
		{
			return TargetAccess.ReadMemory (Address, size);
		}

		// <summary>
		//   Same than ReadMemory(), but returns a byte[] array.
		// </summary>
		public byte[] ReadBuffer (int size)
		{
			return ReadMemory (size).Contents;
		}

		public virtual void WriteBuffer (byte[] data)
		{
			TargetAccess.WriteBuffer (Address, data);
		}

		public virtual void WriteAddress (TargetAddress address)
		{
			TargetAccess.WriteAddress (Address, address);
		}

		public virtual TargetAddress ReadAddress ()
		{
			if (HasAddress)
				return Address;

			byte[] data = ReadBuffer (TargetAccess.TargetAddressSize);

			long address;
			if (TargetAccess.TargetAddressSize == 4)
				address = BitConverter.ToInt32 (data, 0);
			else if (TargetAccess.TargetAddressSize == 8)
				address = BitConverter.ToInt64 (data, 0);
			else
				throw new InternalError ();

			return new TargetAddress (TargetAccess.AddressDomain, address);
		}

		public ITargetAccess TargetAccess {
			get {
				return frame.TargetAccess;
			}
		}

		public event LocationEventHandler LocationInvalidEvent;

		protected virtual void OnLocationInvalidEvent ()
		{
			if (LocationInvalidEvent != null)
				LocationInvalidEvent (this);
		}

		// <summary>
		//   Clones this location, but adds `offset' to its offset.
		//   Note that this'll just affect the new location's `Offset' property -
		//   if you use this for reference types, this won't modify the address
		//   which gets dereferenced.
		//   This is usually what you want to access the data at `offset' within
		//   the variable's contents (for instance to skip a header or access an
		//   array element).
		// </summary>
		public virtual TargetLocation GetLocationAtOffset (long offset, bool dereference)
		{
			TargetLocation new_location = Clone (offset);
			if (!dereference)
				return new_location;

			TargetAddress address = TargetAccess.ReadAddress (new_location.Address);
			return new RelativeTargetLocation (this,  address);
		}

		protected abstract TargetLocation Clone (long offset);

		public object Clone ()
		{
			return Clone (0);
		}

		protected virtual string MyToString ()
		{
			return "";
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3:x}{4})",
					      GetType (), frame.TargetAddress, is_byref, offset,
					      MyToString ());
		}
	}
}
