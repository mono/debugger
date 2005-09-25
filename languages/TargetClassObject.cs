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

		public abstract TargetClassObject Parent {
			get;
		}

		public abstract TargetObject GetField (TargetAccess target, int index);

		public abstract void SetField (TargetAccess target, int index, TargetObject obj);
	}
}
