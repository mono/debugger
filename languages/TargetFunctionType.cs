namespace Mono.Debugger.Languages
{
	public abstract class TargetFunctionType : TargetType, ITargetFunctionType
	{
		protected TargetFunctionType (ILanguage language)
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

		ITargetType ITargetFunctionType.ReturnType {
			get { return ReturnType; }
		}

		ITargetType[] ITargetFunctionType.ParameterTypes {
			get { return ParameterTypes; }
		}

		// <summary>
		//   The current programming language's native representation of
		//   a method.
		// </summary>
		public abstract object MethodHandle {
			get;
		}

		public abstract ITargetStructType DeclaringType {
			get;
		}
	}
}
