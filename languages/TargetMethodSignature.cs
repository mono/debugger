using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages
{
	public abstract class TargetMethodSignature : DebuggerMarshalByRefObject
	{
		public abstract TargetType ReturnType {
			get;
		}

		public abstract TargetType[] ParameterTypes {
			get;
		}
	}
}
