using System;

namespace Mono.Debugger
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

		// <summary>
		//   The current programming language's native representation of
		//   a method.  This is a System.Reflection.MethodInfo for managed
		//   data types.
		// </summary>
		object MethodHandle {
			get;
		}

		ITargetObject InvokeStatic (StackFrame frame, object[] args);
	}
}
