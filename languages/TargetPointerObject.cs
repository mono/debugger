namespace Mono.Debugger.Languages
{
	public abstract class TargetPointerObject : TargetObject
	{
		public new readonly TargetPointerType Type;

		internal TargetPointerObject (TargetPointerType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		// <summary>
		//   The current type of the object pointed to by this pointer.
		//   May only be used if ITargetPointerType.HasStaticType is false.
		// </summary>
		public abstract TargetType GetCurrentType (TargetAccess target);

		// <summary>
		//   If HasDereferencedObject is true, return the dereferenced object.
		// </summary>
		public abstract TargetObject GetDereferencedObject (TargetAccess target);

		public bool HasAddress {
			get { return Location.HasAddress; }
		}

		public TargetAddress Address {
			get { return Location.Address; }
		}

		public abstract TargetObject GetArrayElement (TargetAccess target, int index);
	}
}
