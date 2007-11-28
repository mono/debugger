namespace Mono.Debugger.Languages
{
	public abstract class TargetClassObject : TargetStructObject
	{
		public readonly new TargetClassType Type;

		internal TargetClassObject (TargetClassType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public abstract TargetClassObject GetParentObject (Thread target);

		public abstract TargetClassObject GetCurrentObject (Thread target);

		public abstract TargetObject GetField (Thread target, TargetFieldInfo field);

		public abstract void SetField (Thread target, TargetFieldInfo field,
					       TargetObject obj);
	}
}
