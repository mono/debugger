using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	internal delegate void MethodLoadedHandler (TargetAccess target, Method method);

	public abstract class TargetFunctionType : TargetType
	{
		protected TargetFunctionType (Language language)
			: base (language, TargetObjectKind.Function)
		{ }

		public abstract string FullName {
			get;
		}

		public abstract bool HasSourceCode {
			get;
		}

		public abstract SourceFile SourceFile {
			get;
		}

		public abstract int StartRow {
			get;
		}

		public abstract int EndRow {
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

		public abstract bool IsManaged {
			get;
		}

		public abstract TargetAddress GetMethodAddress (Thread target);

		internal abstract bool InsertBreakpoint (Thread target,
							 FunctionBreakpointHandle handle);

		internal abstract void RemoveBreakpoint (Thread target);
	}
}
