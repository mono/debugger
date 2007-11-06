namespace Mono.Debugger.Languages
{
	public abstract class TargetStructType : TargetType
	{
		protected TargetStructType (Language language, TargetObjectKind kind)
			: base (language, kind)
		{ }

		public abstract Module Module {
			get;
		}

		public abstract TargetClass GetClass (TargetMemoryAccess target);
	}
}
