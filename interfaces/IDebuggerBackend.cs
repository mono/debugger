using System;
using System.IO;

namespace Mono.Debugger
{
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
		//   The ISourceFileFactory which is used to find source files.
		// </summary>
		ISourceFileFactory SourceFileFactory {
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

		void StepInstruction ();

		void NextInstruction ();

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

		IStackFrame[] GetBacktrace (int max_frames, bool full_backtrace);

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
	}
}
