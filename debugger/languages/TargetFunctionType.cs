using System.Runtime.Serialization;

namespace Mono.Debugger.Languages
{
	public abstract class TargetFunctionType : TargetType
	{
		protected TargetFunctionType (Language language)
			: base (language, TargetObjectKind.Function)
		{ }

		public abstract string FullName {
			get;
		}

		public abstract bool IsStatic {
			get;
		}

		public abstract bool IsConstructor {
			get;
		}

		public abstract bool HasReturnValue {
			get;
		}

		public abstract TargetType ReturnType {
			get;
		}

		public abstract TargetType[] ParameterTypes {
			get;
		}

		public abstract MethodSource Source {
			get;
		}

		// <summary>
		//   The current programming language's native representation of
		//   a method.
		// </summary>
		public abstract object MethodHandle {
			get;
		}

		public Module Module {
			get { return DeclaringType.Module; }
		}

		public abstract TargetClassType DeclaringType {
			get;
		}

		public abstract bool IsLoaded {
			get;
		}

		public abstract TargetAddress GetMethodAddress (Thread target);
	}
}
