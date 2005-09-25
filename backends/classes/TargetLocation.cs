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
		protected TargetAccess target;

		protected TargetLocation (TargetAccess target)
		{
			this.target = target;
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
		public abstract TargetAddress Address {
			get;
		}

		internal virtual TargetBlob ReadMemory (TargetAccess target, int size)
		{
			return target.TargetMemoryAccess.ReadMemory (Address, size);
		}

		// <summary>
		//   Same than ReadMemory(), but returns a byte[] array.
		// </summary>
		internal byte[] ReadBuffer (TargetAccess target, int size)
		{
			return ReadMemory (target, size).Contents;
		}

		internal virtual void WriteBuffer (TargetAccess target, byte[] data)
		{
			target.TargetMemoryAccess.WriteBuffer (Address, data);
		}

		internal virtual void WriteAddress (TargetAccess target, TargetAddress address)
		{
			target.TargetMemoryAccess.WriteAddress (Address, address);
		}

		internal TargetAccess TargetAccess {
			get {
				return target;
			}
		}

		internal ITargetInfo TargetInfo {
			get {
				return target.TargetInfo;
			}
		}

		internal ITargetMemoryInfo TargetMemoryInfo {
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
		internal TargetLocation GetLocationAtOffset (long offset)
		{
			if (offset != 0)
				return new RelativeTargetLocation (this, offset);
			else
				return this;
		}

		internal TargetLocation GetDereferencedLocation (TargetAccess target)
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
			return String.Format ("{0} ({1})", GetType (), MyToString ());
		}
	}
}
