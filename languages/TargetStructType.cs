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

		public abstract bool HasParent {
			get;
		}

		internal abstract TargetStructType GetParentType (TargetMemoryAccess target);

		public TargetStructType GetParentType (Thread thread)
		{
			return (TargetStructType) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetParentType (target);
			});
		}

		public abstract TargetClass GetClass (TargetMemoryAccess target);
	}
}
