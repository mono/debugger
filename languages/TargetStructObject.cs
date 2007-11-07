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
	}
}
