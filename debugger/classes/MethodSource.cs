using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public abstract class MethodSource : DebuggerMarshalByRefObject
	{
		public abstract Module Module {
			get;
		}

		public abstract string Name {
			get;
		}

		public abstract bool IsManaged {
			get;
		}

		public abstract TargetClassType DeclaringType {
			get;
		}

		public abstract TargetFunctionType Function {
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

		public abstract Method GetMethod (int domain);
	}
}
