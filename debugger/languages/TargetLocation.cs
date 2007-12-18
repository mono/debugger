using System;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This class represents the `location' of a variable.  The idea is that we do
	//   not always have an address for a variable (for instance if it's stored in a
	//   register) and that an addresses lifetime may be limited.
	// </summary>
	public abstract class TargetLocation
	{
		// <summary>
		//   Whether this variable has an address.  A variable may not have an
		//   address, for instance if it's stored in a register.
		// </summary>
		internal abstract bool HasAddress {
			get;
		}

		internal abstract TargetAddress GetAddress (TargetMemoryAccess target);

		internal virtual TargetBlob ReadMemory (TargetMemoryAccess target, int size)
		{
			return target.ReadMemory (GetAddress (target), size);
		}

		// <summary>
		//   Same than ReadMemory(), but returns a byte[] array.
		// </summary>
		internal byte[] ReadBuffer (TargetMemoryAccess target, int size)
		{
			return ReadMemory (target, size).Contents;
		}

		internal virtual void WriteBuffer (TargetMemoryAccess target, byte[] data)
		{
			target.WriteBuffer (GetAddress (target), data);
		}

		internal virtual void WriteAddress (TargetMemoryAccess target, TargetAddress address)
		{
			target.WriteAddress (GetAddress (target), address);
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

		internal TargetLocation GetDereferencedLocation ()
		{
			return new DereferencedTargetLocation (this);
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
