namespace Mono.Debugger.Languages
{
	public abstract class TargetStructObject : TargetObject
	{
		public readonly new TargetStructType Type;

		internal TargetStructObject (TargetStructType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public TargetStructObject GetParentObject (Thread thread)
		{
			return (TargetStructObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetParentObject (target);
			});
		}

		internal abstract TargetStructObject GetParentObject (TargetMemoryAccess target);

		public TargetStructObject GetCurrentObject (Thread thread)
		{
			if (!type.IsByRef)
				return null;

			return (TargetStructObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetCurrentObject (target);
			});
		}

		internal abstract TargetStructObject GetCurrentObject (TargetMemoryAccess target);
	}
}
