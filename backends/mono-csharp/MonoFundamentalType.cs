using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalType : MonoType
	{
		public MonoFundamentalType (Type type, int size)
			: base (type, size)
		{ }

		public static bool Supports (Type type)
		{
			if (type.IsByRef || !type.IsPrimitive)
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
				return true;

			default:
				return false;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		public override object GetObject (ITargetMemoryReader target_reader)
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

			default:
				throw new InvalidOperationException ();
			}
		}
	}
}
