using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayObject : MonoObject, ITargetArrayObject
	{
		protected new MonoArrayTypeInfo type;

		protected readonly int rank;
		protected readonly int length;
		protected readonly int dimension;
		protected readonly int base_index;
		protected readonly MonoArrayBounds[] bounds;

		public MonoArrayObject (MonoArrayTypeInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
			this.dimension = 0;
			this.rank = type.Type.Rank;

			ITargetInfo target_info = type.Type.File.TargetInfo;

			try {
				ITargetMemoryReader reader = location.ReadMemory (type.Size);

				reader.Offset = 3 * target_info.TargetAddressSize;
				length = reader.BinaryReader.ReadInt32 ();

				if (rank == 1)
					return;

				reader.Offset = 2 * target_info.TargetAddressSize;
				TargetAddress bounds_address = reader.ReadAddress ();
				ITargetMemoryReader breader = location.TargetMemoryAccess.ReadMemory (
					bounds_address, 8 * rank);

				bounds = new MonoArrayBounds [rank];

				for (int i = 0; i < rank; i++) {
					int b_length = breader.BinaryReader.ReadInt32 ();
					int b_lower = breader.BinaryReader.ReadInt32 ();
					bounds [i] = new MonoArrayBounds (b_lower, b_length);
				}
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public MonoArrayObject (MonoArrayObject array, TargetLocation location, int index)
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
				if (rank == 1)
					return 0;

				return bounds [dimension].Lower;
			}
		}

		public int UpperBound {
			get {
				if (rank == 1)
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
					ITargetMemoryReader reader;
					TargetLocation dynamic_location;
					try {
						reader = location.ReadMemory (type.Size);
						GetDynamicSize (reader, location, out dynamic_location);
					} catch (TargetException ex) {
						throw new LocationInvalidException (ex);
					}

					int offset;
					if (type.Type.ElementType.IsByRef)
						offset = index * reader.TargetAddressSize;
					else if (type.ElementType.HasFixedSize)
						offset = index * type.ElementType.Size;
					else
						throw new InvalidOperationException ();

					TargetLocation new_location =
						dynamic_location.GetLocationAtOffset (
							offset, type.Type.ElementType.IsByRef);

					return type.ElementType.GetObject (new_location);
				}

				for (int i = dimension + 1; i < rank; i++)
					index *= bounds [i].Length;

				return new MonoArrayObject (this, location, base_index + index);
			}
		}

		int GetElementSize (ITargetInfo info)
		{
			if (type.Type.ElementType.IsByRef)
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

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			int element_size = GetElementSize (reader);
			dynamic_location = location.GetLocationAtOffset (
				type.Size + element_size * base_index, false);
			return element_size * GetLength ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), type,
					      type.Type.ElementType, length);
		}
	}
}
