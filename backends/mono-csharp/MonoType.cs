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

		bool has_fixed_size;
		int size;

		protected MonoType (Type type, int size)
		{
			this.type = type;
			this.size = size;
			this.has_fixed_size = true;
		}

		protected MonoType (Type type, int size, bool has_fixed_size)
		{
			this.type = type;
			this.size = size;
			this.has_fixed_size = has_fixed_size;
		}

		public static MonoType GetType (Type type, ITargetMemoryAccess memory, TargetAddress address,
						MonoSymbolFileTable table)
		{
			int size = memory.ReadInteger (address);
			ITargetMemoryReader info = memory.ReadMemory (
				address + memory.TargetIntegerSize, size);
			return GetType (type, info, table);
		}

		private static MonoType GetType (Type type, ITargetMemoryReader info,
						 MonoSymbolFileTable table)
		{
			if (type == typeof (void))
				return new MonoOpaqueType (type, 0);

			int size = info.ReadInteger ();
			if (size > 0) {
				if (MonoFundamentalType.Supports (type))
					return new MonoFundamentalType (type, size);
				else
					return new MonoOpaqueType (type, size);
			} else if (size == -1)
				return new MonoOpaqueType (type, 0);

			size = info.BinaryReader.ReadInt32 ();

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

		protected virtual TargetAddress GetAddress (ITargetLocation location,
							    out ITargetMemoryAccess memory)
		{
			return GetAddress (location, out memory, IsByRef);
		}

		internal virtual TargetAddress GetAddress (ITargetLocation location,
							   out ITargetMemoryAccess memory,
							   bool isbyref)
		{
			TargetAddress address = location.Address;
			StackFrame frame = location.Handle as StackFrame;
			if ((frame == null) || (address.Address == 0))
				throw new LocationInvalidException ();

			memory = frame.TargetMemoryAccess;
			if (isbyref) {
				try {
					address = memory.ReadAddress (address);
				} catch {
					throw new LocationInvalidException ();
				}

				if (address.Address == 0)
					throw new LocationInvalidException ();
			}

			return address;
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

		public abstract bool HasObject {
			get;
		}

		public virtual MonoObject GetObject (ITargetLocation location)
		{
			return GetObject (location, IsByRef);
		}

		public abstract MonoObject GetObject (ITargetLocation location, bool isbyref);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}:{5}]", GetType (), TypeHandle,
					      IsByRef, HasFixedSize, Size, HasObject);
		}
	}
}
