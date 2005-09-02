using System;

namespace Mono.Debugger.Languages
{
	public delegate void LocationEventHandler (TargetLocation location);

	// <summary>
	//   This class represents the `location' of a variable.  The idea is that we do
	//   not always have an address for a variable (for instance if it's stored in a
	//   register) and that an addresses lifetime may be limited.
	// </summary>
	[Serializable]
	public abstract class TargetLocation : ICloneable
	{
		protected ITargetAccess target;
		protected StackFrame frame;
		protected bool is_byref;
		bool is_valid;

		protected TargetLocation (StackFrame frame, bool is_byref)
			: this (frame, frame.TargetAccess, is_byref)
		{ }

		protected TargetLocation (StackFrame frame, ITargetAccess target, bool is_byref)
		{
			this.is_byref = is_byref;
			this.target = target;
			this.frame = frame;
			this.is_valid = true;
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
				return address;
			}
		}

		public TargetAddress GlobalAddress {
			get {
				TargetAddress address = Address;
				if (address.IsNull)
					return TargetAddress.Null;

				return new TargetAddress (
					TargetMemoryInfo.GlobalAddressDomain, address.Address);
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

		protected void SetInvalid ()
		{
			is_valid = false;
		}

		protected abstract TargetAddress GetAddress ();

		public virtual TargetBlob ReadMemory (int size)
		{
			return TargetMemoryAccess.ReadMemory (Address, size);
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
			TargetMemoryAccess.WriteBuffer (Address, data);
		}

		public virtual void WriteAddress (TargetAddress address)
		{
			TargetMemoryAccess.WriteAddress (Address, address);
		}

		public virtual TargetAddress ReadAddress ()
		{
			if (HasAddress)
				return Address;

			byte[] data = ReadBuffer (TargetMemoryInfo.TargetAddressSize);

			long address;
			if (TargetMemoryInfo.TargetAddressSize == 4)
				address = BitConverter.ToInt32 (data, 0);
			else if (TargetMemoryInfo.TargetAddressSize == 8)
				address = BitConverter.ToInt64 (data, 0);
			else
				throw new InternalError ();

			return new TargetAddress (TargetMemoryInfo.AddressDomain, address);
		}

		public ITargetAccess TargetAccess {
			get {
				return target;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				return target.TargetMemoryAccess;
			}
		}

		public ITargetInfo TargetInfo {
			get {
				return target.TargetInfo;
			}
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get {
				return target.TargetMemoryInfo;
			}
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
			TargetLocation new_location;
			if (offset != 0)
				new_location = new RelativeTargetLocation (Clone (), offset);
			else
				new_location = Clone ();
			if (!dereference)
				return new_location;

			TargetAddress address = TargetMemoryAccess.ReadAddress (new_location.Address);
			return new AbsoluteTargetLocation (frame, target, address);
		}

		protected abstract TargetLocation Clone ();

		object ICloneable.Clone ()
		{
			return Clone ();
		}

		protected virtual string MyToString ()
		{
			return "";
		}

		public abstract string Print ();

		public override string ToString ()
		{
			if (frame != null)
				return String.Format ("{0} ({1}:{2}:{3})",
						      GetType (), frame.TargetAddress, is_byref,
						      MyToString ());
			else
				return String.Format ("{0} ({1}:{2})",
						      GetType (), is_byref, MyToString ());
		}
	}
}
