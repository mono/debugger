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
	}
}
