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

		public abstract TargetClassObject GetParentObject (TargetAccess target);

		public abstract TargetObject GetField (TargetAccess target, TargetFieldInfo field);

		public abstract void SetField (TargetAccess target, TargetFieldInfo field,
					       TargetObject obj);
	}
}
