namespace Mono.Debugger.Languages
{
	public interface ITargetEnumType : ITargetType
	{
		ITargetFieldInfo Value {
			get ;
		}

		ITargetFieldInfo[] Members {
			get;
		}

		ITargetObject GetMember (StackFrame frame, int index);
	}
}
