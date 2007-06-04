using System;

namespace Mono.Debugger
{
	[Serializable]
	public class TargetInvocationException : Exception
	{
		public TargetInvocationException (string message)
			: base (message)
		{ }
	}
}
