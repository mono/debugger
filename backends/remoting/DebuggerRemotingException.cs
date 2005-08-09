using System;

namespace Mono.Debugger.Remoting
{
	[Serializable]
	public class DebuggerRemotingException : Exception
	{
		public DebuggerRemotingException (string message)
			: base (message)
		{ }

		public DebuggerRemotingException (string format, params object[] args)
			: this (String.Format (format, args))
		{ }
	}
}
