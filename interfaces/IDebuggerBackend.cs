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

	/// <summary>
	///   This denotes a single debuggable target in some debugger.
	///
	///   A debugger implements this interface to denote one single debuggable target
	///   (an application the user is debugging).  If a debugger may debug more than
	///   one application at a time, it will create multiple instances of a class which
	///   implements this interface.
	/// </summary>
	public interface IDebuggerBackend : IDisposable
	{
		// <summary>
		//   Get the state of the target we're debugging.
		// </summary>
		TargetState State {
			get;
		}

		// <summary>
		//   Start/continue the target.
		// </summary>
		void Run ();

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
		//   Aborts the target being debugged, but gives it time to terminate cleanly.
		//   On Unix systems, this'll send a SIGTERM to the target process.
		// </summary>
		void Abort ();

		// <summary>
		//   Forcibly kills the target without giving it any time to terminate.
		//   On Unix systems, this'll send a SIGKILL to the target process.
		// </summary>
		void Kill ();

		// <summary>
		//   Ask the debugger for the current stack frame and
		//   emit a CurrentFrameEvent.
		// </summary>
		void Frame ();

		// <summary>
		//   Single-step and enter into methods.
		// </summary>
		void Step ();

		// <summary>
		//   Single-step, but step over method invocations.
		// </summary>
		void Next ();

		// <summary>
		//   Size of an address in the target.
		// <summary>
		uint TargetAddressSize {
			get;
		}

		// <summary>
		//   Size of an integer in the target.
		// <summary>
		uint TargetIntegerSize {
			get;
		}

		// <summary>
		//   Size of a long integer in the target.
		// <summary>
		uint TargetLongIntegerSize {
			get;
		}

		// <summary>
		//   Read an address from the target's address space at address @address.
		// </summary>
		long ReadAddress (long address);

		// <summary>
		//   Read a single byte from the target's address space at address @address.
		// </summary>
		byte ReadByte (long address);

		// <summary>
		//   Read an integer from the target's address space at address @address.
		// </summary>
		uint ReadInteger (long address);

		// <summary>
		//   Read a signed integer from the target's address space at address @address.
		// </summary>
		int ReadSignedInteger (long address);

		// <summary>
		//   Read a long int from the target's address space at address @address.
		// </summary>
		long ReadLongInteger (long address);

		// <summary>
		//   Read an zero-terminated string from the target's address space at
		//   address @address.
		// </summary>
		string ReadString (long address);

		// <summary>
		//   Returns the contents of the integer register @name.
		// </summary>
		int ReadIntegerRegister (string name);

		// <summary>
		//   Adds a breakpoint at the specified target location.
		// </summary>
		IBreakPoint AddBreakPoint (ITargetLocation location);

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

		// <summary>
		//   This event is emitted each time the application
		//   stops.  A GUI sould listen to this event to
		//   highlight the source line corresponding to the
		//   current stack frame.
		// </summary>
		event StackFrameHandler CurrentFrameEvent;

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
