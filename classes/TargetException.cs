using System;

namespace Mono.Debugger
{
	public class TargetException : Exception
	{
		public TargetException (string message)
			: base (message)
		{ }
	}

	public class NoTargetException : TargetException
	{
		public NoTargetException ()
			: base ("There is no program to debug")
		{ }
	}

	public class TargetNotStoppedException : TargetException
	{
		public TargetNotStoppedException ()
			: base ("The target is currently running, but it must be stopped to perform " +
				"the requested operation")
		{ }
	}

	public class NoStackException : TargetException
	{
		public NoStackException ()
			: this ("No stack")
		{ }

		public NoStackException (string message)
			: base (message)
		{ }
	}
}
