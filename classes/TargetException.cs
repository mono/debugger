using System;

namespace Mono.Debugger
{
	public enum TargetExceptionType {
		CannotStartTarget,
		AlreadyHaveTarget,
		NoTarget,
		NotStopped,
		NoStack,
		NoMethod,
		NoSuchBreakpoint,
		AlreadyHaveBreakpoint,
		NoSuchRegister,
		MemoryAccess,
		NotImplemented,
		IOError,
		SymbolTable,
		InvocationException,
		LocationInvalid
	}

	public class TargetException : Exception
	{
		public readonly TargetExceptionType Type;

		public TargetException (TargetExceptionType type, string message)
			: base (message)
		{
			this.Type = type;
		}

		public TargetException (TargetExceptionType type, string format,
					params object[] args)
			: this (type, String.Format (format, args))
		{ }

		public TargetException (TargetExceptionType type)
			: this (type, GetMessage (type))
		{ }

		protected static string GetMessage (TargetExceptionType type)
		{
			switch (type) {
			case TargetExceptionType.CannotStartTarget:
				return "Cannot start target.";
			case TargetExceptionType.AlreadyHaveTarget:
				return "Already have a program to debug.";
			case TargetExceptionType.NoTarget:
				return "No target.";
			case TargetExceptionType.NotStopped:
				return "The target is currently running, but it must be " +
					"stopped to perform the requested operation.";
			case TargetExceptionType.NoStack:
				return "No stack.";
			case TargetExceptionType.NoMethod:
				return "Cannot get bounds of current method.";
			case TargetExceptionType.NoSuchBreakpoint:
				return "No such breakpoint.";
			case TargetExceptionType.AlreadyHaveBreakpoint:
				return "Already have a breakpoint at this location.";
			case TargetExceptionType.NoSuchRegister:
				return "No such register.";
			case TargetExceptionType.MemoryAccess:
				return "Memory access.";
			case TargetExceptionType.NotImplemented:
				return "Requested feature not implemented on this platform.";
			case TargetExceptionType.IOError:
				return "Unknown I/O error.";
			case TargetExceptionType.SymbolTable:
				return "Symbol table error.";
			case TargetExceptionType.InvocationException:
				return "Error while invoking a method in the target.";
			case TargetExceptionType.LocationInvalid:
				return "Location is invalid.";
			default:
				return "Unknown error";
			}
		}
	}

	public class LocationInvalidException : TargetException
	{
		public LocationInvalidException ()
			: this ("Location is invalid.")
		{ }

		public LocationInvalidException (string message)
			: base (TargetExceptionType.LocationInvalid, message)
		{ }

		public LocationInvalidException (TargetException ex)
			: this (GetExceptionText (ex))
		{ }

		protected static string GetExceptionText (TargetException ex)
		{
			if ((ex is TargetMemoryException) || (ex is LocationInvalidException))
				return ex.Message;
			else
				return String.Format ("{0}: {1}", ex.GetType ().Name, ex.Message);
		}
	}
}
