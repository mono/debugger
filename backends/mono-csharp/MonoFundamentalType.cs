using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalType : MonoClass, ITargetFundamentalType
	{
		protected readonly Heap Heap;

		public MonoFundamentalType (Type type, int size, TargetBinaryReader info, MonoSymbolTable table)
			: this (type, size, info, table, true)
		{ }

		protected MonoFundamentalType (Type type, int size, TargetBinaryReader info, MonoSymbolTable table,
					       bool has_fixed_size)
			: base (TargetObjectKind.Fundamental, type, size, false, info, table, has_fixed_size)
		{
			this.Heap = table.Language.DataHeap;
		}

		public static bool Supports (Type type)
		{
			if (type.IsByRef)
				type = type.GetElementType ();

			if (!type.IsPrimitive) {
				if ((type == typeof (IntPtr)) || (type == typeof (UIntPtr)))
					return true;

				return false;
			}

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

		Type ITargetFundamentalType.Type {
			get {
				return type;
			}
		}

		public override bool IsByRef {
			get {
				return type.IsByRef;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFundamentalObject (this, location);
		}

		public virtual byte[] CreateObject (object obj)
		{
			switch (System.Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
				return BitConverter.GetBytes (Convert.ToBoolean (obj));

			case TypeCode.Char:
				return BitConverter.GetBytes (Convert.ToChar (obj));

			case TypeCode.SByte:
				return BitConverter.GetBytes (Convert.ToSByte (obj));

			case TypeCode.Byte:
				return BitConverter.GetBytes (Convert.ToByte (obj));

			case TypeCode.Int16:
				return BitConverter.GetBytes (Convert.ToInt16 (obj));

			case TypeCode.UInt16:
				return BitConverter.GetBytes (Convert.ToUInt16 (obj));

			case TypeCode.Int32:
				return BitConverter.GetBytes (Convert.ToInt32 (obj));

			case TypeCode.UInt32:
				return BitConverter.GetBytes (Convert.ToUInt32 (obj));

			case TypeCode.Int64:
				return BitConverter.GetBytes (Convert.ToInt64 (obj));

			case TypeCode.UInt64:
				return BitConverter.GetBytes (Convert.ToUInt64 (obj));

			case TypeCode.Single:
				return BitConverter.GetBytes (Convert.ToSingle (obj));

			case TypeCode.Double:
				return BitConverter.GetBytes (Convert.ToDouble (obj));

			default:
				throw new ArgumentException ();
			}
		}

		internal virtual MonoFundamentalObjectBase CreateInstance (StackFrame frame, object obj)
		{
			TargetLocation location = Heap.Allocate (frame, Size);
			frame.TargetAccess.WriteBuffer (location.Address, CreateObject (obj));

			return new MonoFundamentalObject (this, location);
		}
	}
}
