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

			try {
				TargetBinaryReader reader = location.ReadMemory (type.Size).GetReader ();

				reader.Position = 3 * reader.TargetInfo.TargetAddressSize;
				length = reader.ReadInt32 ();

				if (rank == 1)
					return;

				reader.Position = 2 * reader.TargetInfo.TargetAddressSize;
				TargetAddress bounds_address = new TargetAddress (
					location.TargetMemoryInfo.AddressDomain, reader.ReadAddress ());
				TargetBinaryReader breader = location.TargetMemoryAccess.ReadMemory (
					bounds_address, 8 * rank).GetReader ();

				bounds = new MonoArrayBounds [rank];

				for (int i = 0; i < rank; i++) {
					int b_length = breader.ReadInt32 ();
					int b_lower = breader.ReadInt32 ();
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
					TargetBlob blob;
					TargetLocation dynamic_location;
					try {
						blob = location.ReadMemory (type.Size);
						GetDynamicSize (blob, location, out dynamic_location);
					} catch (TargetException ex) {
						throw new LocationInvalidException (ex);
					}

					int offset;
					if (type.Type.ElementType.IsByRef)
						offset = index * blob.TargetInfo.TargetAddressSize;
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

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			int element_size = GetElementSize (blob.TargetInfo);
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
