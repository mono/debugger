using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoType : ITargetType
	{
		internal enum TypeKind {
			Fundamental = 1,
			String,
			SzArray,
			Array,
			Pointer,
			Enum,
			Object,
			Struct,
			Class
		};

		protected Type type;
		protected static MonoObjectType ObjectType;
		protected static MonoClassType ObjectClass;
		protected static ITargetMethodInfo[] ObjectClassMethods;
		protected static ITargetMethodInfo ObjectToString;
		protected readonly TargetObjectKind kind;
		protected readonly TargetAddress klass;

		bool has_fixed_size;
		int size;

		protected MonoType (TargetObjectKind kind, Type type, int size, TargetAddress klass)
		{
			this.type = type;
			this.size = size;
			this.kind = kind;
			this.klass = klass;
			this.has_fixed_size = true;
		}

		protected MonoType (TargetObjectKind kind, Type type, int size, TargetAddress klass,
				    bool has_fixed_size)
		{
			this.type = type;
			this.size = size;
			this.kind = kind;
			this.klass = klass;
			this.has_fixed_size = has_fixed_size;
		}

		public TargetObjectKind Kind {
			get {
				return kind;
			}
		}

		public static MonoType GetType (Type type, int offset, MonoSymbolTable table)
		{
			byte[] data = table.GetTypeInfo (offset);
			TargetBinaryReader info = new TargetBinaryReader (data, table.TargetInfo);
			return GetType (type, info, table);
		}

		private static MonoType GetType (Type type, TargetBinaryReader info, MonoSymbolTable table)
		{
			if (type == typeof (void))
				return new MonoOpaqueType (type, 0);

			int kind = info.ReadByte ();
			if (kind == 0)
				return new MonoOpaqueType (type, 0);

			TargetAddress klass = new TargetAddress (table.GlobalAddressDomain, info.ReadAddress ());
			int size = info.ReadInt32 ();

			switch ((TypeKind) kind) {
			case TypeKind.Fundamental:
				if (MonoFundamentalType.Supports (type))
					return new MonoFundamentalType (type, size, klass, info, table);
				else
					throw new InternalError ("Unknown fundamental type: {0} {1}",
								 type, Type.GetTypeCode (type));

			case TypeKind.String:
				return new MonoStringType (type, size, klass, info, table);

			case TypeKind.SzArray:
				return new MonoArrayType (type, size, klass, info, false, table);

			case TypeKind.Array:
				return new MonoArrayType (type, size, klass, info, true, table);

			case TypeKind.Pointer:
				return new MonoPointerType (type, size, klass);

			case TypeKind.Enum:
				return new MonoEnumType (type, size, klass, info, table);

			case TypeKind.Struct:
				return new MonoStructType (type, size, klass, info, table);

			case TypeKind.Class:
				return new MonoClassType (type, size, klass, info, table);

			case TypeKind.Object:
				if (ObjectType == null) {
					ObjectType = new MonoObjectType (typeof (object), size, klass, table);
					ObjectClass = new MonoClassType (typeof (object), size, klass, info, table);
					ObjectClassMethods = ObjectClass.Methods;
					foreach (ITargetMethodInfo method in ObjectClassMethods) {
						if (method.Name == "ToString")
							ObjectToString = method;
					}
					if (ObjectToString == null)
						throw new InternalError ();
				}

				return ObjectType;

			default:
				return new MonoOpaqueType (type, size);
			}
		}

		public virtual string Name {
			get {
				return type.Name;
			}
		}

		public object TypeHandle {
			get {
				return type;
			}
		}

		public TargetAddress KlassAddress {
			get {
				return klass;
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
