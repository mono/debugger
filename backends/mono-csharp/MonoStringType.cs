using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringType : MonoType
	{
		int size;
		int length_offset;
		int length_size;
		int data_offset;

		static int max_string_length = 100;

		public MonoStringType (Type type, TargetBinaryReader info)
			: base (type)
		{
			int size_field = - info.ReadInt32 ();
			if (size_field != 5)
				throw new InternalError ();
			if (info.ReadByte () != 1)
				throw new InternalError ();
			size = info.ReadByte ();
			length_offset = info.ReadByte ();
			length_size = info.ReadByte ();
			data_offset = info.ReadByte ();
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return type == typeof (string);
		}

		public static int MaxStringLength {
			get {
				return max_string_length;
			}

			set {
				max_string_length = value;
			}
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
				return true;
			}
		}

		protected override object GetObject (ITargetMemoryReader target_reader)
		{
			byte[] contents = target_reader.Contents;

			int length = contents.Length / 2;
			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) ((contents [2*i + 1] << 8) + contents [2*i]);

			return new String (retval);
		}

		public override object GetObject (ITargetMemoryAccess memory, TargetAddress address)
		{
			address = memory.ReadAddress (address);
			TargetBinaryReader reader = memory.ReadMemory (address, Size).BinaryReader;

			reader.Position = length_offset;
			int length = (int) reader.ReadInteger (length_size);

			if (length > max_string_length)
				length = max_string_length;

			ITargetMemoryReader string_reader = memory.ReadMemory (
				address + data_offset, 2 * length);

			return GetObject (string_reader);
		}
	}
}
