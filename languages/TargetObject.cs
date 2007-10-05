using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetObject : DebuggerMarshalByRefObject
	{
		internal readonly TargetLocation Location;
		protected TargetType type;
		string type_name;

		internal TargetObject (TargetType type, TargetLocation location)
		{
			this.type = type;
			this.Location = location;
			this.type_name = type.Name;
		}

		public TargetType Type {
			get {
				return type;
			}
		}

		public string TypeName {
			get {
				return type_name;
			}
		}

		internal void SetTypeName (string type_name)
		{
			this.type_name = type_name;
		}

		public TargetObjectKind Kind {
			get {
				return type.Kind;
			}
		}

		public bool HasAddress {
			get { return Location.HasAddress; }
		}

		public TargetAddress GetAddress (Thread target)
		{
			if (!Location.HasAddress)
				throw new InvalidOperationException ();

			return Location.GetAddress (target);
		}

		internal abstract long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location);

		public virtual string Print (Thread target)
		{
			return ToString ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Type);
		}
	}
}
