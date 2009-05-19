using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	#region Keep in sync with ServerCommandError in backends/server/server.h
	[Serializable]
	public enum TargetError {
		None			= 0,

		UnknownError		= 1,
		InternalError,
		NoTarget,
		AlreadyHaveTarget,
		CannotStartTarget,
		NotStopped,
		AlreadyStopped,
		RecursiveCall,
		NoSuchBreakpoint,
		NoSuchRegister,
		DebugRegisterOccupied,
		MemoryAccess,
		NotImplemented,
		IOError,
		NoCallbackFrame,

		NoStack			= 101,
		NoMethod,
		MethodNotLoaded,
		ClassNotInitialized,
		AlreadyHaveBreakpoint,
		SymbolTable,
		InvocationException,
		LocationInvalid,
		CannotDetach,
		InvalidReturn,
		NoInvocation
	}
	#endregion

	[Serializable]
	public class TargetException : Exception, ISerializable
	{
		public readonly TargetError Type;

		public TargetException (TargetError type, string message)
			: base (message)
		{
			this.Type = type;
		}

		public TargetException (TargetError type, string format,
					params object[] args)
			: this (type, String.Format (format, args))
		{ }

		public TargetException (TargetError type)
			: this (type, GetMessage (type))
		{ }

		protected static string GetMessage (TargetError type)
		{
			switch (type) {
			case TargetError.UnknownError:
				return "Unknown error.";
			case TargetError.NoTarget:
				return "No target.";
			case TargetError.AlreadyHaveTarget:
				return "Already have a program to debug.";
			case TargetError.CannotStartTarget:
				return "Cannot start target.";
			case TargetError.NotStopped:
				return "The target is currently running, but it must be " +
					"stopped to perform the requested operation.";
			case TargetError.AlreadyStopped:
				return "The target is already stopped.";
			case TargetError.RecursiveCall:
				return "Internal error: recursive call";
			case TargetError.NoSuchBreakpoint:
				return "No such breakpoint.";
			case TargetError.NoSuchRegister:
				return "No such register.";
			case TargetError.DebugRegisterOccupied:
				return "Cannot insert hardware breakpoint/watchpoint: " +
					"all debugging registers are already occupied.";
			case TargetError.MemoryAccess:
				return "Memory access.";
			case TargetError.NotImplemented:
				return "Requested feature not implemented on this platform.";
			case TargetError.IOError:
				return "Unknown I/O error.";
			case TargetError.NoStack:
				return "No stack.";
			case TargetError.NoMethod:
				return "Cannot get bounds of current method.";
			case TargetError.MethodNotLoaded:
				return "Method not loaded.";
			case TargetError.ClassNotInitialized:
				return "Class not initialized.";
			case TargetError.AlreadyHaveBreakpoint:
				return "Already have a breakpoint at this location.";
			case TargetError.SymbolTable:
				return "Symbol table error.";
			case TargetError.InvocationException:
				return "Error while invoking a method in the target.";
			case TargetError.LocationInvalid:
				return "Location is invalid.";
			case TargetError.CannotDetach:
				return "Cannot detach from this target because we did not " +
					"attach to it.";
			case TargetError.InvalidReturn:
				return "Cannot return from this kind of stack frame.";
			case TargetError.NoInvocation:
				return "No invocation found.";
			default:
				return "Unknown error";
			}
		}

		protected TargetException (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{
			Type = (TargetError) info.GetValue ("Type", typeof (TargetError));
		}

                public override void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("Type", Type);
			base.GetObjectData (info, context);
		}
	}

	[Serializable]
	public class LocationInvalidException : TargetException
	{
		public LocationInvalidException ()
			: this ("Location is invalid.")
		{ }

		public LocationInvalidException (string message)
			: base (TargetError.LocationInvalid, message)
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

		protected LocationInvalidException (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{
		}

                public override void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData (info, context);
		}
	}
}
