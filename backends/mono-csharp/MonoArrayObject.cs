using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoArrayObject : MonoObject, ITargetArrayObject
	{
		protected new MonoArrayType type;

		protected readonly int rank;
		protected readonly int length;
		protected readonly int dimension;
		protected readonly int base_index;
		protected readonly MonoArrayBounds[] bounds;

		public MonoArrayObject (MonoArrayType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
			this.dimension = 0;
			this.rank = type.Rank;

			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			try {
				ITargetMemoryReader reader = memory.ReadMemory (address, type.Size);

				reader.Offset = type.LengthOffset;
				length = (int) reader.BinaryReader.ReadInteger (type.LengthSize);

				if (rank == 0)
					return;

				reader.Offset = type.BoundsOffset;
				TargetAddress bounds_address = reader.ReadAddress ();
				ITargetMemoryReader breader = memory.ReadMemory (
					bounds_address, type.BoundsSize * rank);

				bounds = new MonoArrayBounds [rank];

				for (int i = 0; i < rank; i++) {
					breader.Offset = i * type.BoundsSize + type.BoundsLowerOffset;
					int b_lower = (int) breader.BinaryReader.ReadInteger (
						type.BoundsLowerSize);
					breader.Offset = i * type.BoundsSize + type.BoundsLengthOffset;
					int b_length = (int) breader.BinaryReader.ReadInteger (
						type.BoundsLengthSize);
					bounds [i] = new MonoArrayBounds (b_lower, b_length);
				}
			} catch {
				throw new LocationInvalidException ();
			}
		}

		public MonoArrayObject (MonoArrayObject array, ITargetLocation location, int index)
			: base (array.type.SubArrayType, location)
		{
			this.type = array.type.SubArrayType;
			this.rank = array.rank;
			this.length = array.length;
			this.dimension = array.dimension + 1;
			this.base_index = index;
			this.bounds = array.bounds;
		}

		public int LowerBound {
			get {
				if (rank == 0)
					return 0;

				return bounds [dimension].Lower;
			}
		}

		public int UpperBound {
			get {
				if (rank == 0)
					return length;

				return bounds [dimension].Lower + bounds [dimension].Length;
			}
		}

		public ITargetObject this [int index] {
			get {
				if ((index < LowerBound) || (index >= UpperBound))
					throw new ArgumentException ();
				index -= LowerBound;

				if (dimension + 1 >= rank) {
					ITargetMemoryAccess memory;
					TargetAddress address = GetAddress (location, out memory);

					TargetAddress dynamic_address;
					try {
						ITargetMemoryReader reader = memory.ReadMemory (
							address, type.Size);
						GetDynamicSize (reader, address, out dynamic_address);
					} catch {
						throw new LocationInvalidException ();
					}

					if (type.ElementType.IsByRef)
						dynamic_address += index * memory.TargetAddressSize;
					else if (type.ElementType.HasFixedSize)
						dynamic_address += index * type.ElementType.Size;
					else
						throw new InvalidOperationException ();

					ITargetLocation new_location = new RelativeTargetLocation (
						location, dynamic_address);

					return type.ElementType.GetObject (new_location);
				}

				for (int i = dimension + 1; i < rank; i++)
					index *= bounds [i].Length;

				return new MonoArrayObject (this, location, base_index + index);
			}
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return false;
			}
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}

		int GetElementSize (ITargetInfo info)
		{
			if (type.ElementType.IsByRef)
				return info.TargetAddressSize;
			else if (type.ElementType.HasFixedSize)
				return type.ElementType.Size;
			else
				throw new InvalidOperationException ();
		}

		int GetLength ()
		{
			int length = UpperBound - LowerBound;
			for (int i = dimension + 1; i < rank; i++)
				length *= bounds [i].Length;
			return length;
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			int element_size = GetElementSize (reader);
			dynamic_address = address + type.DataOffset + element_size * base_index;
			return element_size * GetLength ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), type,
					      type.ElementType, length);
		}
	}
}
