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
		void Continue (TargetAddress until);

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
		TargetAddress CurrentFrame {
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

		// <remarks>
		//   The following two methods are more or less private.
		// </remarks>
		long CallMethod (TargetAddress method, long method_argument);
		TargetAddress SimpleLookup (string name);

		long[] GetRegisters (int[] registers);

		TargetAddress[] GetBacktrace ();

		// <summary>
		//   Returns a disassembler for the current target.
		// </summary>
		IDisassembler Disassembler {
			get;
		}

		// <summary>
		//   Gets the IArchitecture for the current target.
		// </summary>
		IArchitecture Architecture {
			get;
		}

		// <summary>
		//   The symbol table from native executables and shared libraries.
		// </summary>
		ISymbolTableCollection SymbolTable {
			get;
		}
	}
}

