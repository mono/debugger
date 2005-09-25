namespace Mono.Debugger.Languages
{
	public abstract class TargetEnumType : TargetType
	{
		public TargetEnumType (ILanguage language)
			: base (language, TargetObjectKind.Enum)
		{ }

		public abstract TargetFieldInfo Value {
			get;
		}

		public abstract TargetFieldInfo[] Members {
			get;
		}

		public abstract TargetObject GetMember (StackFrame frame, int index);
	}
}
