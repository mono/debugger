using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalType : MonoType
	{
		int size;

		public MonoFundamentalType (Type type, TargetBinaryReader info)
			: base (type)
		{
			size = info.ReadInt32 ();
			// Supports() already checked this.
			if (size <= 0)
				throw new InternalError ();
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			if (!type.IsPrimitive)
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

		object DoGetObject (ITargetMemoryAccess memory, TargetAddress address)
		{
			switch (System.Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
				return memory.ReadByte (address) != 0;

			case TypeCode.Char:
				return BitConverter.ToChar (memory.ReadBuffer (address, 2), 0);

			case TypeCode.SByte:
				return (sbyte) memory.ReadByte (address);

			case TypeCode.Byte:
				return (byte) memory.ReadByte (address);

			case TypeCode.Int16:
				return BitConverter.ToInt16 (memory.ReadBuffer (address, 2), 0);

			case TypeCode.UInt16:
				return BitConverter.ToUInt16 (memory.ReadBuffer (address, 2), 0);

			case TypeCode.Int32:
				return BitConverter.ToInt32 (memory.ReadBuffer (address, 4), 0);

			case TypeCode.UInt32:
				return BitConverter.ToUInt32 (memory.ReadBuffer (address, 4), 0);

			case TypeCode.Int64:
				return BitConverter.ToInt64 (memory.ReadBuffer (address, 8), 0);

			case TypeCode.UInt64:
				return BitConverter.ToUInt64 (memory.ReadBuffer (address, 8), 0);

			case TypeCode.Single:
				return BitConverter.ToSingle (memory.ReadBuffer (address, 4), 0);

			case TypeCode.Double:
				return BitConverter.ToDouble (memory.ReadBuffer (address, 8), 0);

			default:
				throw new InvalidOperationException ();
			}
		}

		protected override MonoObject GetObject (ITargetMemoryAccess memory, ITargetLocation location)
		{
			return new MonoObject (this, DoGetObject (memory, location.Address));
		}
	}
}
