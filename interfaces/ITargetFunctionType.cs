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
		//   a method.  This is a System.Reflection.MethodInfo for managed
		//   data types.
		// </summary>
		object MethodHandle {
			get;
		}

		ITargetObject InvokeStatic (StackFrame frame, ITargetObject[] args,
					    bool debug);
	}
}
