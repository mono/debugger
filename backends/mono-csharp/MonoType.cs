using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoType : ITargetType
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
			ClassInfo
		};

		protected Type type;
		protected readonly TargetObjectKind kind;

		bool has_fixed_size;
		int size;

		protected MonoType (TargetObjectKind kind, Type type, int size)
		{
			this.type = type;
			this.size = size;
			this.kind = kind;
			this.has_fixed_size = true;
		}

		protected MonoType (TargetObjectKind kind, Type type, int size, bool has_fixed_size)
		{
			this.type = type;
			this.size = size;
			this.kind = kind;
			this.has_fixed_size = has_fixed_size;
		}

		public TargetObjectKind Kind {
			get {
				return kind;
			}
		}

		public static MonoType GetType (Type type, TargetBinaryReader info, MonoSymbolFile table)
		{
			int kind = info.ReadByte ();
			if (kind == 0)
				throw new InternalError ();

			int size = info.ReadInt32 ();

			switch ((TypeKind) kind) {
			case TypeKind.Fundamental:
				if (MonoFundamentalType.Supports (type))
					return new MonoFundamentalType (type, size, info, table);
				else
					throw new InternalError ("Unknown fundamental type: {0} {1}",
								 type, Type.GetTypeCode (type));

			case TypeKind.String:
				return new MonoStringType (type, size, info, table);

			case TypeKind.SzArray:
				return new MonoArrayType (type, size, info, table, false);

			case TypeKind.Array:
				return new MonoArrayType (type, size, info, table, true);

			case TypeKind.Pointer:
				return new MonoPointerType (type, size, info, table);

			case TypeKind.Enum:
				return new MonoEnumType (type, size, info, table);

			case TypeKind.Struct:
				return new MonoClass (TargetObjectKind.Struct, type, size, false, info, table, true);

			case TypeKind.Class:
				return new MonoClass (TargetObjectKind.Class, type, size, false, info, table, true);

			case TypeKind.ClassInfo:
				return MonoClass.GetClass (type, size, info, table);

			case TypeKind.Object:
				if (type != typeof (object))
					throw new InternalError ();

				return new MonoObjectType (type, size, info, table);

			case TypeKind.Unknown:
				return new MonoOpaqueType (type, size);

			default:
				throw new InternalError ("KIND: {0}", kind);
			}
		}

		public virtual string Name {
			get {
				return type.FullName;
			}
		}

		object ITargetType.TypeHandle {
			get {
				return type;
			}
		}

		public Type TypeHandle {
			get {
				return type;
			}
		}

		public abstract bool IsByRef {
			get;
		}

		public virtual bool HasFixedSize {
			get {
				return has_fixed_size;
			}
		}

		public virtual int Size {
			get {
				return size;
			}
		}

		public virtual bool CheckValid (TargetLocation location)
		{
			return !location.HasAddress || !location.Address.IsNull;
		}

		public abstract MonoObject GetObject (TargetLocation location);

		ITargetObject ITargetType.GetObject (TargetLocation location)
		{
			return GetObject (location);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}]", GetType (), Name,
					      IsByRef, HasFixedSize, Size);
		}
	}
}
