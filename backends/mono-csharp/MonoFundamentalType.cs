using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalType : MonoType
	{
		int size;

		public MonoFundamentalType (Type type, int stack_size, TargetBinaryReader info)
			: base (type)
		{
			if (type.IsByRef) {
				size = info.ReadInt32 ();
				// Supports() already checked this.
				if (size <= 0)
					throw new InternalError ();
			} else
				size = stack_size;
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			if (!type.IsPrimitive)
				return false;

			if (type.IsByRef && (info == null))
				return false;

			switch (Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
			case TypeCode.Char:
			case TypeCode.SByte:
			case TypeCode.Byte:
			case TypeCode.Int16:
			case TypeCode.UInt16:
			case TypeCode.Int32:
			case TypeCode.UInt32:
			case TypeCode.Int64:
			case TypeCode.UInt64:
			case TypeCode.Single:
			case TypeCode.Double:
				return true;

			default:
				return false;
			}
		}

		public override bool HasFixedSize {
			get {
				return true;
			}
		}

		public override int Size {
			get {
				return size;
			}
		}

		public override bool IsByRef {
			get {
				return type.IsByRef;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		protected override object GetObject (ITargetMemoryReader target_reader)
		{
			TargetBinaryReader reader = target_reader.BinaryReader;

			switch (Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
				return reader.PeekByte () != 0;

			case TypeCode.Char:
				return (char) reader.PeekInt16 ();

			case TypeCode.SByte:
				return (sbyte) reader.PeekByte ();

			case TypeCode.Byte:
				return (byte) reader.PeekByte ();

			case TypeCode.Int16:
				return (short) reader.PeekInt16 ();

			case TypeCode.UInt16:
				return (ushort) reader.PeekInt16 ();

			case TypeCode.Int32:
				return (int) reader.PeekInt32 ();

			case TypeCode.UInt32:
				return (int) reader.PeekInt32 ();

			case TypeCode.Int64:
				return (long) reader.ReadInt64 ();

			case TypeCode.UInt64:
				return (ulong) reader.ReadInt64 ();

			case TypeCode.Single: {
				byte[] bits = BitConverter.GetBytes (reader.ReadInt32 ());
				return BitConverter.ToSingle (bits, 0);
			}

			case TypeCode.Double:
				return BitConverter.Int64BitsToDouble (reader.ReadInt64 ());

			default:
				throw new InvalidOperationException ();
			}
		}
	}
}
