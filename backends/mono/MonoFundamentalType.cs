using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : MonoType, ITargetFundamentalType
	{
		protected readonly int size;
		protected readonly TargetAddress klass_address;
		protected readonly FundamentalKind fundamental_kind;
		protected readonly Cecil.ITypeDefinition typedef;

		public MonoFundamentalType (MonoSymbolFile file, Cecil.ITypeDefinition type,
					    FundamentalKind kind, int size, TargetAddress klass)
			: base (file.MonoLanguage, TargetObjectKind.Fundamental)
		{
			this.typedef = type;
			this.fundamental_kind = kind;
			this.size = size;
			this.klass_address = klass;

			file.MonoLanguage.AddClass (klass_address, this);
		}

		public override string Name {
			get { return typedef.FullName; }
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
			TargetAddress address = Language.AllocateMemory (frame.TargetAccess, size);
			frame.TargetAccess.TargetMemoryAccess.WriteBuffer (address, CreateObject (obj));

			TargetLocation location = new AbsoluteTargetLocation (frame.TargetAccess, address);
			return new MonoFundamentalObject (this, location);
		}

		internal virtual MonoFundamentalObject CreateInstance (ITargetAccess target, object obj)
		{
			TargetAddress address = Language.AllocateMemory (target, size);
			target.TargetMemoryAccess.WriteBuffer (address, CreateObject (obj));

			TargetLocation location = new AbsoluteTargetLocation (target, address);
			return new MonoFundamentalObject (this, location);
		}

		public override bool HasFixedSize {
			get { return FundamentalKind != FundamentalKind.String; }
		}

		public override int Size {
			get { return size; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFundamentalObject (this, location);
		}
	}
}
