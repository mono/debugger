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

		public override MonoObject GetObject (ITargetLocation location)
		{
			return new MonoFundamentalObject (this, location);
		}
	}
}
