using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal struct MonoArrayBounds
	{
		public readonly int Lower;
		public readonly int Length;

		public MonoArrayBounds (int lower, int length)
		{
			this.Lower = lower;
			this.Length = length;
		}
	}

	internal class MonoArrayType : MonoType
	{
		int size;
		int rank;
		int length_offset;
		int length_size;
		int data_offset;
		int bounds_offset;
		int bounds_size;
		int bounds_lower_offset;
		int bounds_lower_size;
		int bounds_length_offset;
		int bounds_length_size;
		MonoType element_type;

		public MonoArrayType (Type type, ITargetMemoryAccess memory, TargetBinaryReader info)
			: base (type)
		{
			int size_field = - info.ReadInt32 ();
			int atype = info.ReadByte ();
			if (atype == 2) {
				// MONO_TYPE_SZARRAY
				if (size_field != 5)
					throw new InternalError ();
			} else if (atype == 3) {
				// MONO_TYPE_ARRAY
				if (size_field != 12 + memory.TargetAddressSize)
					throw new InternalError ();
			} else
				throw new InternalError ();

			size = info.ReadByte ();
			length_offset = info.ReadByte ();
			length_size = info.ReadByte ();
			data_offset = info.ReadByte ();

			if (atype == 3) {
				// MONO_TYPE_ARRAY
				rank = info.ReadByte ();
				bounds_offset = info.ReadByte ();
				bounds_size = info.ReadByte ();
				bounds_lower_offset = info.ReadByte ();
				bounds_lower_size = info.ReadByte ();
				bounds_length_offset = info.ReadByte ();
				bounds_length_size = info.ReadByte ();
			}

			long element_type_info = new TargetAddress (memory, info.ReadAddress ());
			element_type = GetType (type.GetElementType (), 0, memory, element_type_info);
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return type.IsArray;
		}

		public override bool HasFixedSize {
			get {
				return false;
			}
		}

		public override int Size {
			get {
				return size;
			}
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public override bool HasObject {
			get {
				return element_type.HasObject &&
					(element_type.IsByRef || element_type.HasFixedSize);
			}
		}

		internal MonoType ElementType {
			get {
				return element_type;
			}
		}

		protected override MonoObject GetObject (ITargetMemoryAccess memory, ITargetLocation location)
		{
			TargetAddress address = location.Address;
			ITargetMemoryReader reader = memory.ReadMemory (address, size);

			reader.Offset = length_offset;
			int length = (int) reader.BinaryReader.ReadInteger (length_size);

			ITargetLocation new_location = new RelativeTargetLocation (
				location, address + data_offset);

			if (rank == 0)
				return new MonoArray (this, length, new_location);

			reader.Offset = bounds_offset;
			TargetAddress bounds_address = reader.ReadAddress ();
			ITargetMemoryReader bounds = memory.ReadMemory (
				bounds_address, bounds_size * rank);

			MonoArrayBounds[] abounds = new MonoArrayBounds [rank];

			for (int i = 0; i < rank; i++) {
				bounds.Offset = i * bounds_size + bounds_lower_offset;
				int b_lower = (int) bounds.BinaryReader.ReadInteger (bounds_lower_size);
				bounds.Offset = i * bounds_size + bounds_length_offset;
				int b_length = (int) bounds.BinaryReader.ReadInteger (bounds_length_size);
				abounds [i] = new MonoArrayBounds (b_lower, b_length);
			}

			return new MonoArray (this, length, abounds, 0, 0, new_location);
		}
	}
}
