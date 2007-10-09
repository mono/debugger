namespace Mono.Debugger.Languages
{
	public abstract class TargetGenericInstanceObject : TargetObject
	{
		public readonly new TargetGenericInstanceType Type;

		protected TargetGenericInstanceObject (TargetGenericInstanceType type,
						       TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}
	}
}
