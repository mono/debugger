using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayObject : TargetArrayObject
	{
		public MonoArrayObject (MonoArrayType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override void DoGetArrayBounds (Thread target)
		{
			TargetBinaryReader reader = Location.ReadMemory (target, type.Size).GetReader ();

			reader.Position = 3 * reader.TargetInfo.TargetAddressSize;
			int length = reader.ReadInt32 ();

			if (Rank == 1) {
				bounds = new ArrayBounds [1];
				bounds [0] = new ArrayBounds (0, length);
				return;
			}

			reader.Position = 2 * reader.TargetInfo.TargetAddressSize;
			TargetAddress bounds_address = new TargetAddress (
				target.AddressDomain, reader.ReadAddress ());
			TargetBinaryReader breader = target.ReadMemory (
				bounds_address, 8 * Rank).GetReader ();

			bounds = new ArrayBounds [Rank];

			for (int i = 0; i < Rank; i++) {
				int b_length = breader.ReadInt32 ();
				int b_lower = breader.ReadInt32 ();
				bounds [i] = new ArrayBounds (b_lower, b_length);
			}
		}

		public override TargetObject GetElement (Thread target, int[] indices)
		{
			int offset = GetArrayOffset (target, indices);

			TargetBlob blob;
			TargetLocation dynamic_location;
			try {
				blob = Location.ReadMemory (target, Type.Size);
				GetDynamicSize (target, blob, Location, out dynamic_location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}

			TargetLocation new_loc = dynamic_location.GetLocationAtOffset (offset);

			if (Type.ElementType.IsByRef)
				new_loc = new_loc.GetDereferencedLocation (target);

			if (new_loc.HasAddress && new_loc.Address.IsNull)
				return new MonoNullObject (Type.ElementType, new_loc);

			return Type.ElementType.GetObject (new_loc);
		}

		public override void SetElement (Thread target, int[] indices,
						 TargetObject obj)
		{
			int offset = GetArrayOffset (target, indices);

			TargetBlob blob;
			TargetLocation dynamic_location;
			try {
				blob = Location.ReadMemory (target, Type.Size);
				GetDynamicSize (target, blob, Location, out dynamic_location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}

			TargetLocation new_loc = dynamic_location.GetLocationAtOffset (offset);

			Type.ElementType.SetObject (target, new_loc, obj);
		}

		int GetElementSize (TargetInfo info)
		{
			if (Type.ElementType.IsByRef)
				return info.TargetAddressSize;
			else if (Type.ElementType.HasFixedSize)
				return Type.ElementType.Size;
			else
				throw new InvalidOperationException ();
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			int element_size = GetElementSize (blob.TargetInfo);
			dynamic_location = location.GetLocationAtOffset (Type.Size);
			return element_size * GetLength (target);
		}

		public override string Print (Thread target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.Address);
			else
				return String.Format ("{0}", Location);
		}
	}
}
