namespace Mono.Debugger.Languages
{
	public abstract class TargetGenericInstanceObject : TargetClassObject
	{
		public readonly new TargetGenericInstanceType Type;

		internal TargetGenericInstanceObject (TargetGenericInstanceType type,
						      TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}
	}
}
