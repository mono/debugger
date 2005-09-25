namespace Mono.Debugger.Languages
{
	public interface ITargetEnumType : ITargetType
	{
		TargetFieldInfo Value {
			get ;
		}

		TargetFieldInfo[] Members {
			get;
		}

		ITargetObject GetMember (StackFrame frame, int index);
	}
}
