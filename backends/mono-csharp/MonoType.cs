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

		public static MonoType GetType (Type type, int size)
		{
			if (MonoFundamentalType.Supports (type, null))
				return new MonoFundamentalType (type, size, null);

			return new MonoOpaqueType (type, size);
		}

		public static MonoType GetType (Type type, int size, TargetBinaryReader reader)
		{
			if (MonoFundamentalType.Supports (type, reader))
				return new MonoFundamentalType (type, size, reader);
			else if (MonoStringType.Supports (type, reader))
				return new MonoStringType (type, reader);

			return GetType (type, size);
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

		protected abstract object GetObject (ITargetMemoryReader reader);

		public virtual object GetObject (ITargetMemoryAccess memory, TargetAddress address)
		{
			Console.WriteLine ("GET OBJECT: {0} {1} {2}", IsByRef, address, Size);

			if (IsByRef) {
				address = memory.ReadAddress (address);
				Console.WriteLine ("BY REF: {0}", address);
			}

			ITargetMemoryReader reader = memory.ReadMemory (address, Size);
			return GetObject (reader);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}:{5}]", GetType (), TypeHandle,
					      IsByRef, HasFixedSize, Size, HasObject);
		}
	}
}
