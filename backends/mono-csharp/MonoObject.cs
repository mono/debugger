using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoObject : ITargetObject
	{
		protected MonoType type;
		protected MonoTargetLocation location;
		bool is_valid;

		public MonoObject (MonoType type, MonoTargetLocation location)
		{
			this.type = type;
			this.location = location;
			is_valid = true;
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

		public abstract bool HasObject {
			get;
		}

		public virtual object Object {
			get {
				if (!HasObject)
					throw new InvalidOperationException ();

				try {
					ITargetMemoryReader reader;
					if (type.HasFixedSize)
						reader = location.ReadMemory (type.Size);
					else
						reader = GetDynamicContents (location, MaximumDynamicSize);

					return GetObject (reader, location);
				} catch {
					is_valid = false;
					throw new LocationInvalidException ();
				}
			}
		}

		public virtual byte[] RawContents {
			get {
				try {
					Console.WriteLine ("RAW CONTENTS: {0} {1}", location, type.Size);
					return location.ReadBuffer (type.Size);
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

				try {
					MonoTargetLocation dynamic_location;
					ITargetMemoryReader reader = location.ReadMemory (type.Size);
					return GetDynamicSize (reader, location, out dynamic_location);
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

			try {
				return GetDynamicContents (location, max_size).Contents;
			} catch {
				is_valid = false;
				throw new LocationInvalidException ();
			}
		}

		protected virtual ITargetMemoryReader GetDynamicContents (MonoTargetLocation location,
									  int max_size)
		{
			try {
				MonoTargetLocation dynamic_location;
				ITargetMemoryReader reader = location.ReadMemory (type.Size);
				long size = GetDynamicSize (reader, location, out dynamic_location);

				if ((max_size > 0) && (size > (long) max_size))
					size = max_size;

				return dynamic_location.ReadMemory ((int) size);
			} catch {
				is_valid = false;
				throw new LocationInvalidException ();
			}
		}

		protected abstract long GetDynamicSize (ITargetMemoryReader reader,
							MonoTargetLocation location,
							out MonoTargetLocation dynamic_location);

		protected abstract object GetObject (ITargetMemoryReader reader, MonoTargetLocation location);

		public override string ToString ()
		{
			if (HasObject)
				return String.Format ("{0} [{1}:{2}]", GetType (), Type, Object);
			else
				return String.Format ("{0} [{1}]", GetType (), Type);
		}
	}
}
