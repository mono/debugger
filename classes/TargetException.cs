using System;

namespace Mono.Debugger
{
	public class TargetException : Exception
	{
		public TargetException (string message)
			: base (message)
		{ }
	}
}
