using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoArrayType : MonoType
	{
		int size;
		int length_offset;
		int length_size;
		int data_offset;
		MonoType element_type;

		public MonoArrayType (Type type, ITargetMemoryAccess memory, TargetBinaryReader info)
			: base (type)
		{
			int size_field = - info.ReadInt32 ();
			if (info.ReadByte () != 2)
				throw new InternalError ();
			size = info.ReadByte ();
			length_offset = info.ReadByte ();
			length_size = info.ReadByte ();
			data_offset = info.ReadByte ();

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
				return element_type.HasObject && element_type.HasFixedSize;
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
			TargetBinaryReader reader = memory.ReadMemory (address, size).BinaryReader;

			reader.Position = length_offset;
			int length = (int) reader.ReadInteger (length_size);

			ITargetLocation new_location = new RelativeTargetLocation (
				location, address + data_offset);

			return new MonoArray (this, length, new_location);
		}
	}
}
