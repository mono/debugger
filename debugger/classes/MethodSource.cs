using System;

namespace Mono.Debugger
{
	internal delegate void MethodLoadedHandler (TargetMemoryAccess target, MethodSource method,
						    object user_data);

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

		internal abstract ILoadHandler RegisterLoadHandler (Thread target,
								    MethodLoadedHandler handler,
								    object user_data);
	}
}
