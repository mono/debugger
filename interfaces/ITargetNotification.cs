using System;
using System.IO;

namespace Mono.Debugger
{
	public delegate void TargetExitedHandler ();
	public delegate void TargetOutputHandler (string output);
	public delegate void DebuggerErrorHandler (object sender, string message, Exception e);
	public delegate void StateChangedHandler (TargetState new_state, int arg);

	/// <summary>
	///   State of the target (the application we're debugging).
	/// </summary>
	public enum TargetState
	{
		// <summary>
		//   There is no target to debug.
		// </summary>
		NO_TARGET,

		// <summary>
		//   The debugger is busy doing some things.
		// </summary>
		BUSY,

		// <summary>
		//   The target is running.
		// </summary>
		RUNNING,

		// <summary>
		//   The target is stopped.
		// </summary>
		STOPPED,

		// <summary>
		//   The target has exited.
		// </summary>
		EXITED,

		// <summary>
		//   Undebuggable daemon thread.
		// </summary>
		DAEMON,

		// <summary>
		//   This is a core file.
		// </summary>
		CORE_FILE,

		LAST = CORE_FILE
	}

	public interface ITargetNotification
	{
		// <summary>
		//   Get the state of the target we're debugging.
		// </summary>
		TargetState State {
			get;
		}

		// <summary>
		//   This event is emitted when the target we're currently debugging has sent any
		//   output to stdout.
		// </summary>
		event TargetOutputHandler TargetOutput;

		// <summary>
		//   This event is emitted when the target we're currently debugging has sent any
		//   error messages to stderr.
		// </summary>
		event TargetOutputHandler TargetError;

		// <summary>
		//   This event is emitted by the debugger to write diagnostic messages and errors.
		// </summary>
		event TargetOutputHandler DebuggerOutput;

		event DebuggerErrorHandler DebuggerError;

		// <summary>
		//   This event is emitted when the state of the target we're currently debugging
		//   has changed, for instance when the target has stopped or exited.
		// </summary>
		event StateChangedHandler StateChanged;

		// <summary>
		//   This event is emitted when the state of the target we're currently debugging
		//   has changed, for instance when the target has stopped or exited.
		// </summary>
		event TargetExitedHandler TargetExited;
	}
}
