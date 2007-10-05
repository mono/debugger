namespace Mono.Debugger.Languages
{
	public abstract class TargetClassObject : TargetObject
	{
		public readonly new TargetClassType Type;

		internal TargetClassObject (TargetClassType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public abstract TargetClassObject GetParentObject (Thread target);

		public abstract TargetClassObject GetCurrentObject (TargetMemoryAccess target);

		public abstract TargetObject GetField (TargetMemoryAccess target, TargetFieldInfo field);

		public abstract void SetField (TargetAccess target, TargetFieldInfo field,
					       TargetObject obj);
	}
}
