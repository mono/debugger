namespace Mono.Debugger.Languages
{
	public abstract class TargetEnumObject : TargetObject
	{
		public new readonly TargetEnumType Type;

		internal TargetEnumObject (TargetEnumType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public abstract TargetObject Value {
			get;
		}
	}
}
