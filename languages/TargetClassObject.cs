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

		public abstract TargetObject GetField (int index);

		public abstract void SetField (int index, TargetObject obj);
	}
}
