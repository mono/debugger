using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoType : ITargetType
	{
		protected Type type;

		protected MonoType (Type type)
		{
			this.type = type;
		}

		public static MonoType GetType (Type type, int size, ITargetMemoryAccess memory,
						TargetBinaryReader reader)
		{
			if (MonoFundamentalType.Supports (type, reader))
				return new MonoFundamentalType (type, reader);
			else if (MonoStringType.Supports (type, reader))
				return new MonoStringType (type, reader);
			else if (MonoArrayType.Supports (type, reader))
				return new MonoArrayType (type, memory, reader);
			else
				return new MonoOpaqueType (type, size);
		}

		public static MonoType GetType (Type type, long type_size, ITargetMemoryAccess memory,
						long address)
		{
			TargetAddress taddress = new TargetAddress (memory, address);
			int size = memory.ReadInteger (taddress);
			taddress += memory.TargetIntegerSize;

			ITargetMemoryReader reader = memory.ReadMemory (taddress, size);
			return GetType (type, 0, memory, reader.BinaryReader);
		}

		public object TypeHandle {
			get {
				return type;
			}
		}

		public abstract bool IsByRef {
			get;
		}

		public abstract bool HasFixedSize {
			get;
		}

		public abstract int Size {
			get;
		}

		public abstract bool HasObject {
			get;
		}

		public virtual MonoObject GetElementObject (ITargetLocation location, int index)
		{
			if (!HasObject)
				throw new InvalidOperationException ();

			TargetAddress address = location.Address;
			StackFrame frame = location.Handle as StackFrame;
			if (frame == null)
				throw new LocationInvalidException ();

			int offset = 0;
			ITargetMemoryAccess memory = frame.TargetMemoryAccess;
			if (IsByRef) {
				address = memory.ReadAddress (address);
				offset = index * memory.TargetAddressSize;
			} else if (HasFixedSize)
				offset = index * Size;
			else if (index > 0)
				throw new InvalidOperationException ();

			ITargetLocation new_location = new RelativeTargetLocation (
				location, address + offset);

			return GetObject (memory, new_location);

		}

		public virtual MonoObject GetObject (ITargetLocation location)
		{
			return GetElementObject (location, 0);
		}

		protected abstract MonoObject GetObject (ITargetMemoryAccess memory, ITargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}:{5}]", GetType (), TypeHandle,
					      IsByRef, HasFixedSize, Size, HasObject);
		}
	}
}
