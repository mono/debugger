using System;

namespace Mono.Debugger
{
	public class TargetInvocationException : Exception
	{
		public TargetInvocationException (string message)
			: base (message)
		{ }
	}
}
