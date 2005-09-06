using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : MonoType, ITargetFundamentalType
	{
		protected readonly Heap Heap;
		protected readonly int size;
		protected readonly TargetAddress klass_address;
		protected readonly FundamentalKind fundamental_kind;

		public MonoFundamentalType (MonoSymbolFile file, Type type,
					    FundamentalKind kind, int size, TargetAddress klass)
			: base (file, TargetObjectKind.Fundamental, type)
		{
			this.fundamental_kind = kind;
			this.size = size;
			this.klass_address = klass;
			this.Heap = file.MonoLanguage.DataHeap;
		}

		protected override MonoTypeInfo CreateTypeInfo ()
		{
			return new MonoFundamentalTypeInfo (this, size, klass_address);
		}

		public override bool IsByRef {
			get {
				switch (fundamental_kind) {
				case FundamentalKind.Object:
				case FundamentalKind.String:
				case FundamentalKind.IntPtr:
				case FundamentalKind.UIntPtr:
					return true;

				default:
					return false;
				}
			}
		}

		public FundamentalKind FundamentalKind {
			get {
				return fundamental_kind;
			}
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
		}

		public virtual byte[] CreateObject (object obj)
		{
			switch (fundamental_kind) {
			case FundamentalKind.Boolean:
				return BitConverter.GetBytes (Convert.ToBoolean (obj));

			case FundamentalKind.Char:
				return BitConverter.GetBytes (Convert.ToChar (obj));

			case FundamentalKind.SByte:
				return BitConverter.GetBytes (Convert.ToSByte (obj));

			case FundamentalKind.Byte:
				return BitConverter.GetBytes (Convert.ToByte (obj));

			case FundamentalKind.Int16:
				return BitConverter.GetBytes (Convert.ToInt16 (obj));

			case FundamentalKind.UInt16:
				return BitConverter.GetBytes (Convert.ToUInt16 (obj));

			case FundamentalKind.Int32:
				return BitConverter.GetBytes (Convert.ToInt32 (obj));

			case FundamentalKind.UInt32:
				return BitConverter.GetBytes (Convert.ToUInt32 (obj));

			case FundamentalKind.Int64:
				return BitConverter.GetBytes (Convert.ToInt64 (obj));

			case FundamentalKind.UInt64:
				return BitConverter.GetBytes (Convert.ToUInt64 (obj));

			case FundamentalKind.Single:
				return BitConverter.GetBytes (Convert.ToSingle (obj));

			case FundamentalKind.Double:
				return BitConverter.GetBytes (Convert.ToDouble (obj));

			case FundamentalKind.IntPtr:
			case FundamentalKind.UIntPtr:
				IntPtr ptr = (IntPtr) obj;
				if (IntPtr.Size == 4)
					return BitConverter.GetBytes (ptr.ToInt32 ());
				else
					return BitConverter.GetBytes (ptr.ToInt64 ());
			default:
				throw new InvalidOperationException ();
			}
		}

		internal virtual MonoFundamentalObjectBase CreateInstance (StackFrame frame, object obj)
		{
			TargetLocation location = Heap.Allocate (frame.TargetAccess, size);
			frame.TargetAccess.TargetMemoryAccess.WriteBuffer (
				location.Address, CreateObject (obj));

			return new MonoFundamentalObject ((MonoFundamentalTypeInfo)type_info, location);
		}

		internal virtual MonoFundamentalObject CreateInstance (ITargetAccess target, object obj)
		{
			TargetLocation location = Heap.Allocate (target, size);
			target.TargetMemoryAccess.WriteBuffer (location.Address, CreateObject (obj));

			return new MonoFundamentalObject ((MonoFundamentalTypeInfo)type_info, location);
		}
	}
}
