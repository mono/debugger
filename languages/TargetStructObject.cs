namespace Mono.Debugger.Languages
{
	public abstract class TargetStructObject : TargetObject
	{
		new public TargetStructType Type;

		internal TargetStructObject (TargetStructType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public TargetClassObject GetParentObject (Thread thread)
		{
			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetParentObject (target);
			});
		}

		internal abstract TargetClassObject GetParentObject (TargetMemoryAccess target);

		public TargetClassObject GetCurrentObject (Thread thread)
		{
			if (!type.IsByRef)
				return null;

			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetCurrentObject (target);
			});
		}

		internal abstract TargetClassObject GetCurrentObject (TargetMemoryAccess target);
	}
}
