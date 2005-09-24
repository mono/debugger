using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayObject : TargetObject, ITargetArrayObject
	{
		protected new MonoArrayType type;

		protected readonly int rank;
		protected readonly int length;
		protected readonly MonoArrayBounds[] bounds;

		public MonoArrayObject (MonoArrayType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
			this.rank = type.Rank;

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

		ITargetArrayType ITargetArrayObject.Type {
			get { return type; }
		}

		public int GetLowerBound (int dimension)
		{
			if ((dimension < 0) || (dimension >= rank))
				throw new ArgumentException ();

			if (rank == 1)
				return 0;

			return bounds [dimension].Lower;
		}

		public int GetUpperBound (int dimension)
		{
			if ((dimension < 0) || (dimension >= rank))
				throw new ArgumentException ();

			if (rank == 1)
				return length;

			return bounds [dimension].Lower + bounds [dimension].Length;
		}

		int GetArrayOffset (int[] indices)
		{
			if (indices.Length != rank)
				throw new ArgumentException ();

			if (rank > 1) {
				for (int i = 0; i < rank; i++) {
					if (indices [i] < bounds [i].Lower)
						throw new ArgumentException ();

					indices [i] -= bounds [i].Lower;

					if (indices [i] >= bounds [i].Length)
						throw new ArgumentException ();
				}
			} else if ((indices [0] < 0) || (indices [0] > length))
				throw new ArgumentException ();

			int index = indices [0];
			for (int i = 1; i < rank; i++)
				index = index * bounds [i].Length + indices [i];

			if (type.ElementType.IsByRef)
				return index * location.TargetInfo.TargetAddressSize;
			else if (type.ElementType.HasFixedSize)
				return index * type.ElementType.Size;
			else
				throw new InvalidOperationException ();
		}

		public ITargetObject GetElement (ITargetAccess target, int[] indices)
		{
			int offset = GetArrayOffset (indices);

			TargetBlob blob;
			TargetLocation dynamic_location;
			try {
				blob = location.ReadMemory (type.Size);
				GetDynamicSize (blob, location, out dynamic_location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}

			TargetLocation new_loc = dynamic_location.GetLocationAtOffset (offset);

			if (type.ElementType.IsByRef)
				new_loc = new_loc.GetDereferencedLocation (target);

			if (new_loc.HasAddress && new_loc.Address.IsNull)
				return new MonoNullObject (type.ElementType, new_loc);

			return type.ElementType.GetObject (new_loc);
		}

		public void SetElement (ITargetAccess target, int[] indices, ITargetObject obj)
		{
			int offset = GetArrayOffset (indices);

			TargetBlob blob;
			TargetLocation dynamic_location;
			try {
				blob = location.ReadMemory (type.Size);
				GetDynamicSize (blob, location, out dynamic_location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}

			TargetLocation new_loc = dynamic_location.GetLocationAtOffset (offset);

			type.ElementType.SetObject (new_loc, (TargetObject) obj);
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
			int length = GetUpperBound (0) - GetLowerBound (0);
			for (int i = 1; i < rank; i++)
				length *= bounds [i].Length;
			return length;
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			int element_size = GetElementSize (blob.TargetInfo);
			dynamic_location = location.GetLocationAtOffset (type.Size);
			return element_size * GetLength ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), type,
					      type.ElementType, length);
		}
	}
}
