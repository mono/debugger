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
		public TargetType GetCurrentType (Thread thread)
		{
			return (TargetType) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetCurrentType (target);
			});
		}

		internal abstract TargetType GetCurrentType (TargetMemoryAccess target);

		// <summary>
		//   If HasDereferencedObject is true, return the dereferenced object.
		// </summary>
		public TargetObject GetDereferencedObject (Thread thread)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetDereferencedObject (target);
			});
		}

		internal abstract TargetObject GetDereferencedObject (TargetMemoryAccess target);

		public TargetObject GetArrayElement (Thread thread, int index)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetArrayElement (target, index);
			});
		}

		internal abstract TargetObject GetArrayElement (TargetMemoryAccess target, int index);
	}
}
