using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This class represents the `location' of a variable.  The idea is that we do
	//   not always have an address for a variable (for instance if it's stored in a
	//   register) and that an addresses lifetime may be limited.
	// </summary>
	internal abstract class TargetLocation
	{
		protected ITargetAccess target;
		protected StackFrame frame;
		protected bool is_byref;

		protected TargetLocation (StackFrame frame, bool is_byref)
			: this (frame, frame.TargetAccess, is_byref)
		{ }

		protected TargetLocation (StackFrame frame, ITargetAccess target, bool is_byref)
		{
			this.is_byref = is_byref;
			this.target = target;
			this.frame = frame;
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

				// First get the address of this variable.
				try {
					return GetAddress ();
				} catch (TargetException ex) {
					return TargetAddress.Null;
				}
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

			if (address == 0)
				return TargetAddress.Null;

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
		public TargetLocation GetLocationAtOffset (long offset)
		{
			if (offset != 0)
				return new RelativeTargetLocation (this, offset);
			else
				return this;
		}

		public TargetLocation GetDereferencedLocation (ITargetAccess target)
		{
			TargetAddress address = target.TargetMemoryAccess.ReadAddress (Address);
			return new AbsoluteTargetLocation (target, address);
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
