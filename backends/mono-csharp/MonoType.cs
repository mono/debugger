using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoType : ITargetType
	{
		protected Type type;
		protected static MonoObjectType ObjectType;
		protected static MonoClassType ObjectClass;
		protected static ITargetMethodInfo[] ObjectClassMethods;
		protected static ITargetMethodInfo ObjectToString;
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

		public static MonoType GetType (Type type, int offset, MonoSymbolFileTable table)
		{
			byte[] data = table.GetTypeInfo (offset);
			TargetBinaryReader info = new TargetBinaryReader (data, table.TargetInfo);
			return GetType (type, info, table);
		}

		private static MonoType GetType (Type type, TargetBinaryReader info,
						 MonoSymbolFileTable table)
		{
			if (type == typeof (void))
				return new MonoOpaqueType (type, 0);

			int size = info.ReadInt32 ();
			if (size > 0) {
				if (MonoFundamentalType.Supports (type))
					return new MonoFundamentalType (type, size);
				else
					return new MonoOpaqueType (type, size);
			} else if (size == -1)
				return new MonoOpaqueType (type, 0);

			size = info.ReadInt32 ();

			int kind = info.ReadByte ();
			switch (kind) {
			case 1:
				return new MonoStringType (type, size, info);

			case 2:
				return new MonoArrayType (type, size, info, false, table);

			case 3:
				return new MonoArrayType (type, size, info, true, table);

			case 4:
				return new MonoEnumType (type, size, info, table);

			case 5:
				return new MonoStructType (type, size, info, table);

			case 6:
				return new MonoClassType (type, size, info, table);

			case 7:
				if (ObjectType == null) {
					ObjectType = new MonoObjectType (typeof (object), size, table);
					ObjectClass = new MonoClassType (typeof (object), size, info, table);
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

		public abstract MonoObject GetObject (MonoTargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}]", GetType (), TypeHandle,
					      IsByRef, HasFixedSize, Size);
		}
	}
}
