using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetObject : MarshalByRefObject
	{
		internal readonly TargetLocation Location;
		protected TargetType type;

		internal TargetObject (TargetType type, TargetLocation location)
		{
			this.type = type;
			this.Location = location;
		}

		public TargetType Type {
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
				if (!Location.HasAddress)
					return false;
				else
					return Location.Address.IsNull;
			}
		}

		internal abstract long GetDynamicSize (TargetBlob blob, TargetLocation location,
						       out TargetLocation dynamic_location);

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
