using System;
using System.IO;

namespace Mono.Debugger
{
	public delegate void TargetOutputHandler (string otuput);
	public delegate void StateChangedHandler (TargetState new_state);

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
		EXITED
	}

	public interface ITargetNotification
	{
		// <summary>
		//   This event is called when the target we're currently debugging has sent any
		//   output to stdout.
		// </summary>
		event TargetOutputHandler TargetOutput;

		// <summary>
		//   This event is called when the target we're currently debugging has sent any
		//   error messages to stderr.
		// </summary>
		event TargetOutputHandler TargetError;

		// <summary>
		//   This event is called when the state of the target we're currently debugging
		//   has changed, for instance when the target has stopped or exited.
		// </summary>
		event StateChangedHandler StateChanged;
	}

	public interface IInferior : ITargetMemoryAccess, ITargetNotification, IDisposable
	{
		// <summary>
		//   Get the state of the target we're debugging.
		// </summary>
		TargetState State {
			get;
		}

		// <summary>
		//   Continue the target.
		// </summary>
		void Continue ();

		// <summary>
		//   Continue running until we either reach the specified location, hit a
		//   breakpoint or receive a signal.
		// </summary>
		void Continue (ITargetLocation location);

		// <summary>
		//   Aborts the target being debugged, but gives it time to terminate cleanly.
		//   On Unix systems, this'll send a SIGTERM to the target process.
		// </summary>
		void Shutdown ();

		// <summary>
		//   Forcibly kills the target without giving it any time to terminate.
		//   On Unix systems, this'll send a SIGKILL to the target process.
		// </summary>
		void Kill ();

		// <summary>
		//   Get the current target location.
		// </summary>
		ITargetLocation CurrentFrame {
			get;
		}

		// <summary>
		//   Single-step and enter into methods.
		// </summary>
		void Step ();

		// <summary>
		//   Single-step until we leave the specified frame.
		// </summary>
		void Step (IStepFrame frame);

		// <summary>
		//   Single-step, but step over method invocations.
		// </summary>
		void Next ();

		long CallMethod (ITargetLocation method, long method_argument);

		// <summary>
		//   Returns a disassembler for the current target.
		// </summary>
		IDisassembler Disassembler {
			get;
		}
	}


	/// <summary>
	///   This denotes a single debuggable target in some debugger.
	///
	///   A debugger implements this interface to denote one single debuggable target
	///   (an application the user is debugging).  If a debugger may debug more than
	///   one application at a time, it will create multiple instances of a class which
	///   implements this interface.
	/// </summary>
	public interface IDebuggerBackend : ITargetNotification, IDisposable
	{
		// <summary>
		//   The inferior is a handle to the program being debugged while this interface
		//   is more a container which contains higher-level stuff like symbol tables etc.
		//   This may be null if there's currently no program being debugged.
		// </summary>
		IInferior Inferior {
			get;
		}

		// <summary>
		//   Get the state of the target we're debugging.
		// </summary>
		TargetState State {
			get;
		}

		// <summary>
		//   Start the target.
		// </summary>
		void Run ();

		void StepLine ();

		void NextLine ();

		// <summary>
		//   Tell the debugger that we're finished debugging.  This kills the target
		//   and terminates the current debugging session.  If the backend is talking
		//   to an external debugger like gdb, it'll also quit this process.
		//
		//   This is not the same than Dispose() - you're still allowed to call Run()
		//   to start a new debugging session with the same breakpoint settings etc.
		// </summary>
		void Quit ();

		// <summary>
		//   Get the current stack frame.
		// </summary>
		IStackFrame CurrentFrame {
			get;
		}

		// <summary>
		//   Adds a breakpoint at the specified target location.
		// </summary>
		IBreakPoint AddBreakPoint (ITargetLocation location);

		// <summary>
		//   This event is emitted each time the application
		//   stops.  A GUI sould listen to this event to
		//   highlight the source line corresponding to the
		//   current stack frame.
		// </summary>
		event StackFrameHandler FrameChangedEvent;

		// <summary>
		//   This event is emitted when the stack frames have
		//   become invalid.  A GUI should listen to this
		//   event to remove the highlighting of the current
		//   stack frame.
		// </summary>
		event StackFramesInvalidHandler FramesInvalidEvent;

		// <summary>
		//   A source file factory is responsible for finding source files and creating
		//   ISourceFile instances for them.
		// </summary>
		ISourceFileFactory SourceFileFactory {
			get; set;
		}
	}
}
