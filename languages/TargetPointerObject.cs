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
		public abstract TargetType CurrentType {
			get;
		}

		// <summary>
		//   If HasDereferencedObject is true, return the dereferenced object.
		// </summary>
		public abstract TargetObject DereferencedObject {
			get;
		}

		// <summary>
		//   Dereference the pointer and read @size bytes from the location it
		//   points to.  Only allowed for non-typesafe pointers.
		// </summary>
		public abstract byte[] GetDereferencedContents (int size);

		public bool HasAddress {
			get { return Location.HasAddress; }
		}

		public TargetAddress Address {
			get { return Location.Address; }
		}

		public abstract TargetObject GetArrayElement (TargetAccess target, int index);
	}
}
