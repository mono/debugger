using System;

namespace Mono.Debugger
{
	public class TargetInvocationException : TargetException
	{
		public TargetInvocationException (string message)
			: base (message)
		{ }
	}
}
