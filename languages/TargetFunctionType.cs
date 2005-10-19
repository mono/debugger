namespace Mono.Debugger.Languages
{
	public abstract class TargetFunctionType : TargetType
	{
		protected TargetFunctionType (Language language)
			: base (language, TargetObjectKind.Function)
		{ }

		public abstract bool HasReturnValue {
			get;
		}

		public abstract TargetType ReturnType {
			get;
		}

		public abstract TargetType[] ParameterTypes {
			get;
		}

		public abstract SourceMethod Source {
			get;
		}

		// <summary>
		//   The current programming language's native representation of
		//   a method.
		// </summary>
		public abstract object MethodHandle {
			get;
		}

		public abstract TargetClassType DeclaringType {
			get;
		}

		public abstract TargetAddress GetMethodAddress (TargetAccess target);

		public abstract TargetAddress GetVirtualMethod (TargetAccess target,
								ref TargetClassObject instance);
	}
}
