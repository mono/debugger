namespace Mono.Debugger.Languages
{
	public interface ITargetFunctionType : ITargetType
	{
		bool HasReturnValue {
			get;
		}

		ITargetType ReturnType {
			get;
		}

		ITargetType[] ParameterTypes {
			get;
		}

		SourceMethod Source {
			get;
		}

		// <summary>
		//   The current programming language's native representation of
		//   a method.
		// </summary>
		object MethodHandle {
			get;
		}

		ITargetStructType DeclaringType {
			get;
		}
	}
}
