using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoTypeInfo : MarshalByRefObject, ITargetTypeInfo
	{
		internal enum TypeKind {
			Unknown = 1,
			Fundamental,
			String,
			SzArray,
			Array,
			Pointer,
			Enum,
			Object,
			Struct,
			Class,
			ClassInfo,
			Reference
		};

		protected readonly MonoType type;
		protected readonly int size;
		protected readonly TargetAddress KlassAddress;

		protected MonoTypeInfo (MonoType type, TargetBinaryReader info)
		{
			this.type = type;

			size = info.ReadLeb128 ();
			KlassAddress = new TargetAddress (type.File.GlobalAddressDomain, info.ReadAddress ());

			type.File.MonoLanguage.AddClass (KlassAddress, type);
		}

		protected MonoTypeInfo (MonoType type, int size)
			: this (type, size, TargetAddress.Null)
		{ }

		protected MonoTypeInfo (MonoType type, int size, TargetAddress klass_address)
		{
			this.type = type;
			this.size = size;
			this.KlassAddress = klass_address;

			if (!klass_address.IsNull)
				type.File.MonoLanguage.AddClass (klass_address, type);
		}

		ITargetType ITargetTypeInfo.Type {
			get { return type; }
		}

		public MonoType Type {
			get { return type; }
		}

		public abstract bool HasFixedSize {
			get;
		}

		public int Size {
			get { return size; }
		}

		ITargetObject ITargetTypeInfo.GetObject (TargetLocation location)
		{
			return GetObject (location);
		}

		public abstract MonoObject GetObject (TargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), type);
		}
	}
}
