using System;

namespace Mono.Debugger
{
	public abstract class TargetException : Exception
	{
		public TargetException (string message)
			: base (message)
		{ }
	}

	public class CannotStartTargetException : TargetException
	{
		public CannotStartTargetException ()
			: base ("Cannot start target")
		{ }

		public CannotStartTargetException (string message)
			: base (String.Format ("Cannot start target: {0}", message))
		{ }
	}

	public class AlreadyHaveTargetException : TargetException
	{
		public AlreadyHaveTargetException ()
			: base ("I already have a program to debug, can't debug a second one.")
		{ }
	}

	public class NoTargetException : TargetException
	{
		public NoTargetException ()
			: base ("There is no program to debug.")
		{ }
	}

	public class TargetNotStoppedException : TargetException
	{
		public TargetNotStoppedException ()
			: base ("The target is currently running, but it must be stopped to perform " +
				"the requested operation.")
		{ }
	}

	public class NoStackException : TargetException
	{
		public NoStackException ()
			: base ("No stack.")
		{ }
	}

	public class NoMethodException : TargetException
	{
		public NoMethodException ()
			: base ("Cannot get bounds of current method.")
		{ }
	}

	public class NoSuchBreakpointException : TargetException
	{
		public NoSuchBreakpointException ()
			: base ("No such breakpoint.")
		{ }
	}

	public class NoSuchRegisterException : TargetException
	{
		public NoSuchRegisterException ()
			: base ("No such registers.")
		{ }
	}

	public class CannotExecuteCoreFileException : TargetException
	{
		public CannotExecuteCoreFileException ()
			: base ("Cannot execute a core file.")
		{ }
	}

	public class InvalidCoreFileException : TargetException
	{
		public InvalidCoreFileException (string message)
			: base ("Invalid core file: " + message)
		{ }
	}
}
