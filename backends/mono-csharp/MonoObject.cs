using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoObject : ITargetObject
	{
		protected MonoType type;
		protected ITargetLocation location;
		bool is_valid;
		bool isbyref;

		public MonoObject (MonoType type, ITargetLocation location)
			: this (type, location, type.IsByRef)
		{ }

		public MonoObject (MonoType type, ITargetLocation location, bool isbyref)
		{
			this.type = type;
			this.location = location;
			this.isbyref = isbyref;
			is_valid = true;
		}

		public ITargetLocation Location {
			get {
				return location;
			}
		}

		public ITargetType Type {
			get {
				return type;
			}
		}

		public bool IsValid {
			get {
				return is_valid && location.IsValid;
			}
		}

		public bool IsByRef {
			get {
				return isbyref;
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

				try {
					ITargetMemoryReader reader;
					if (type.HasFixedSize)
						reader = memory.ReadMemory (address, type.Size);
					else
						reader = GetDynamicContents (
							memory, address, MaximumDynamicSize);

					return GetObject (reader, address);
				} catch {
					is_valid = false;
					throw new LocationInvalidException ();
				}
			}
		}

		public virtual byte[] RawContents {
			get {
				ITargetMemoryAccess memory;
				TargetAddress address = GetAddress (location, out memory);

				try {
					return memory.ReadBuffer (address, type.Size);
				} catch {
					is_valid = false;
					throw new LocationInvalidException ();
				}
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

				try {
					TargetAddress dynamic_address;
					ITargetMemoryReader reader = memory.ReadMemory (address, type.Size);
					return GetDynamicSize (reader, address, out dynamic_address);
				} catch {
					is_valid = false;
					throw new LocationInvalidException ();
				}
			}
		}

		public virtual byte[] GetRawDynamicContents (int max_size)
		{
			if (type.HasFixedSize)
				throw new InvalidOperationException ();

			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			try {
				return GetDynamicContents (memory, address, max_size).Contents;
			} catch {
					is_valid = false;
				throw new LocationInvalidException ();
			}
		}

		protected virtual ITargetMemoryReader GetDynamicContents (ITargetMemoryAccess memory,
									  TargetAddress address,
									  int max_size)
		{
			try {
				TargetAddress dynamic_address;
				ITargetMemoryReader reader = memory.ReadMemory (address, type.Size);
				long size = GetDynamicSize (reader, address, out dynamic_address);

				if ((max_size > 0) && (size > (long) max_size))
					size = max_size;

				return memory.ReadMemory (dynamic_address, (int) size);
			} catch {
					is_valid = false;
				throw new LocationInvalidException ();
			}
		}

		protected virtual TargetAddress GetAddress (ITargetLocation location,
							    out ITargetMemoryAccess memory)
		{
			return type.GetAddress (location, out memory, IsByRef);
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
