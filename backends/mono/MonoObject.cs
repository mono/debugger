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
