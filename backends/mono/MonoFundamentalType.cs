using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : MonoType, ITargetFundamentalType
	{
		protected readonly Heap Heap;
		protected readonly int size;
		protected readonly TargetAddress klass_address;

		public MonoFundamentalType (MonoSymbolFile file, Type type, int size, TargetAddress klass)
			: base (file, TargetObjectKind.Fundamental, type)
		{
			this.size = size;
			this.klass_address = klass;
			this.Heap = file.MonoLanguage.DataHeap;
		}

		protected override MonoTypeInfo CreateTypeInfo ()
		{
			return new MonoFundamentalTypeInfo (this, size, klass_address);
		}

		public override bool IsByRef {
			get { return type.IsByRef; }
		}

		public static bool Supports (Type type)
		{
			if (type.IsByRef)
				type = type.GetElementType ();

			if (!type.IsPrimitive)
				return false;

			if ((type == typeof (IntPtr)) || (type == typeof (UIntPtr)))
				return true;

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

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
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
				if (type == typeof (IntPtr)) {
					IntPtr ptr = (IntPtr) obj;
					if (IntPtr.Size == 4)
						return BitConverter.GetBytes (ptr.ToInt32 ());
					else
						return BitConverter.GetBytes (ptr.ToInt64 ());
				}
				throw new ArgumentException ();
			}
		}
	}
}
