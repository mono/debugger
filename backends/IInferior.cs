using System;
using System.IO;

namespace Mono.Debugger.Backends
{
	public interface IInferiorStackFrame
	{
		IInferior Inferior {
			get;
		}

		TargetAddress Address {
			get;
		}

		TargetAddress ParamsAddress {
			get;
		}

		TargetAddress LocalsAddress {
			get;
		}
	}

	public interface IInferior : ITargetMemoryAccess, ITargetNotification, IDisposable
	{
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
		//   Single-step until we leave the specified frame.
		// </summary>
		void Step (StepFrame frame);

		// <summary>
		//   Stop the target.
		// </summary>
		void Stop ();

		// <remarks>
		//   The following two methods are more or less private.
		// </remarks>
		long CallMethod (TargetAddress method, long method_argument);
		long CallStringMethod (TargetAddress method, long method_argument,
				       string string_argument);
		TargetAddress SimpleLookup (string name);

		long GetRegister (int register);
		long[] GetRegisters (int[] registers);

		IInferiorStackFrame[] GetBacktrace (int max_frames, bool full_backtrace);

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
		ISymbolTable SymbolTable {
			get;
		}

		// <summary>
		//   The application's symbol table.  You should set this property to get
		//   your application's symbol names in a disassembly.
		// </summary>
		ISymbolTable ApplicationSymbolTable {
			get; set;
		}

		IModule[] Modules {
			get;
		}
	}
}

