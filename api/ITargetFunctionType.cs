using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetFunctionType : ITargetType
	{
		string FullName {
			get;
		}

		bool IsStatic {
			get;
		}

		bool IsConstructor {
			get;
		}

		bool HasReturnValue {
			get;
		}

		ITargetType ReturnType {
			get;
		}

		ITargetType[] ParameterTypes {
			get;
		}

		IMethodSource Source {
			get;
		}

		ITargetClassType DeclaringType {
			get;
		}

		bool IsLoaded {
			get;
		}
	}
}
