using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoObject : ITargetObject
	{
		protected MonoType type;
		protected ITargetLocation location;

		public MonoObject (MonoType type, ITargetLocation location)
		{
			this.type = type;
			this.location = location;
		}

		public ITargetType Type {
			get {
				return type;
			}
		}

		public abstract bool HasObject {
			get;
		}

		public virtual object Object {
			get {
				if (!HasObject)
					throw new InvalidOperationException ();

				ITargetMemoryAccess memory;
				TargetAddress address = GetAddress (location, out memory);

				ITargetMemoryReader reader;
				if (type.HasFixedSize)
					reader = memory.ReadMemory (address, type.Size);
				else
					reader = GetDynamicContents (memory, address, MaximumDynamicSize);

				return GetObject (reader, address);
			}
		}

		public virtual byte[] RawContents {
			get {
				ITargetMemoryAccess memory;
				TargetAddress address = GetAddress (location, out memory);

				return memory.ReadBuffer (address, type.Size);
			}
		}

		protected virtual int MaximumDynamicSize {
			get {
				return -1;
			}
		}

		public virtual long DynamicSize {
			get {
				if (type.HasFixedSize)
					throw new InvalidOperationException ();

				ITargetMemoryAccess memory;
				TargetAddress address = GetAddress (location, out memory);

				TargetAddress dynamic_address;
				ITargetMemoryReader reader = memory.ReadMemory (address, type.Size);
				return GetDynamicSize (reader, address, out dynamic_address);
			}
		}

		public virtual byte[] GetRawDynamicContents (int max_size)
		{
			if (type.HasFixedSize)
				throw new InvalidOperationException ();

			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			return GetDynamicContents (memory, address, max_size).Contents;
		}

		protected virtual ITargetMemoryReader GetDynamicContents (ITargetMemoryAccess memory,
									  TargetAddress address,
									  int max_size)
		{
			TargetAddress dynamic_address;
			ITargetMemoryReader reader = memory.ReadMemory (address, type.Size);
			long size = GetDynamicSize (reader, address, out dynamic_address);

			if ((max_size > 0) && (size > (long) max_size))
				size = max_size;

			return memory.ReadMemory (dynamic_address, (int) size);
		}

		protected virtual TargetAddress GetAddress (ITargetLocation location,
							    out ITargetMemoryAccess memory)
		{
			return type.GetAddress (location, out memory);
		}

		protected abstract long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address);

		protected abstract object GetObject (ITargetMemoryReader reader, TargetAddress address);

		public override string ToString ()
		{
			if (HasObject)
				return String.Format ("{0} [{1}:{2}]", GetType (), Type, Object);
			else
				return String.Format ("{0} [{1}]", GetType (), Type);
		}
	}
}
