using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoObject : MarshalByRefObject, ITargetObject
	{
		protected MonoType type;
		protected TargetLocation location;

		public MonoObject (MonoType type, TargetLocation location)
		{
			this.type = type;
			this.location = location;
		}

		public MonoType Type {
			get {
				return type;
			}
		}

		ITargetType ITargetObject.Type {
			get {
				return type;
			}
		}

		public string TypeName {
			get {
				return type.Name;
			}
		}

		public TargetObjectKind Kind {
			get {
				return type.Kind;
			}
		}

		public bool IsNull {
			get {
				if (!location.HasAddress)
					return false;
				else
					return location.Address.IsNull;
			}
		}

		protected virtual int MaximumDynamicSize {
			get {
				return -1;
			}
		}

		protected virtual TargetBlob GetDynamicContents (TargetLocation location, int max_size)
		{
			try {
				TargetLocation dynamic_location;
				TargetBlob blob = location.ReadMemory (type.Size);
				long size = GetDynamicSize (blob, location, out dynamic_location);

				if ((max_size > 0) && (size > (long) max_size))
					size = max_size;

				return dynamic_location.ReadMemory ((int) size);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location);

		public TargetLocation Location {
			get {
				return location;
			}
		}

		public virtual string Print (ITargetAccess target)
		{
			return ToString ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Type);
		}
	}
}
