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

	public enum ChildEventType {
		CHILD_EXITED = 1,
		CHILD_STOPPED,
		CHILD_SIGNALED,
		CHILD_CALLBACK,
		CHILD_HIT_BREAKPOINT,
		CHILD_MEMORY_CHANGED
	}

	public delegate void ChildEventHandler (ChildEventType message, int arg);

	public interface IInferior : ITargetMemoryAccess, ITargetNotification, IDisposable
	{
		/// <summary>
		///   Continue the target.
		/// </summary>
		void Continue ();

		/// <summary>
		///   Aborts the target being debugged, but gives it time to terminate cleanly.
		///   On Unix systems, this'll send a SIGTERM to the target process.
		/// </summary>
		void Shutdown ();

		/// <summary>
		///   Forcibly kills the target without giving it any time to terminate.
		///   On Unix systems, this'll send a SIGKILL to the target process.
		/// </summary>
		void Kill ();

		/// <summary>
		///   Get the current target location.
		/// </summary>
		TargetAddress CurrentFrame {
			get;
		}

		/// <summary>
		///   Whether the user set a breakpoint at the current instruction.
		/// </summary>
		/// <remarks>
		///   This method only checks whether the user set a breakpoint at the
		///   current instruction, it does not track breakpoint instruction which
		///   were already in the source code.
		/// </remarks>
		bool CurrentInstructionIsBreakpoint {
			get;
		}

		/// <summary>
		///   Single-step one instruction.
		/// </summary>
		void Step ();

		/// <summary>
		///   Stop the target.
		/// </summary>
		void Stop ();

		/// <remarks>
		///   The following two methods are more or less private.
		/// </remarks>
		long CallMethod (TargetAddress method, long method_argument);
		long CallStringMethod (TargetAddress method, long method_argument,
				       string string_argument);
		TargetAddress CallInvokeMethod (TargetAddress invoke_method, TargetAddress method_argument,
						TargetAddress object_argument, TargetAddress[] param_objects,
						out TargetAddress exc_object);
		TargetAddress SimpleLookup (string name);

		void UpdateModules ();

		long GetRegister (int register);
		long[] GetRegisters (int[] registers);
		TargetAddress GetReturnAddress ();

		void SetRegister (int register, long value);
		void SetRegisters (int[] registers, long[] values);

		IInferiorStackFrame[] GetBacktrace (int max_frames, TargetAddress stop);

		TargetMemoryArea[] GetMemoryMaps ();

		int InsertBreakpoint (TargetAddress address);

		void RemoveBreakpoint (int breakpoint);

		void EnableBreakpoint (int breakpoint);

		void DisableBreakpoint (int breakpoint);

		void EnableAllBreakpoints ();

		void DisableAllBreakpoints ();

		TargetAddress MainMethodAddress {
			get;
		}

		/// <summary>
		///   Returns a disassembler for the current target.
		/// </summary>
		IDisassembler Disassembler {
			get;
		}

		/// <summary>
		///   Gets the IArchitecture for the current target.
		/// </summary>
		IArchitecture Architecture {
			get;
		}

		/// <summary>
		///   The symbol table from native executables and shared libraries.
		/// </summary>
		ISymbolTable SymbolTable {
			get;
		}

		/// <summary>
		///   The application's symbol table.  You should set this property to get
		///   your application's symbol names in a disassembly.
		/// </summary>
		ISymbolTable ApplicationSymbolTable {
			get; set;
		}

		DebuggerBackend DebuggerBackend {
			get;
		}

		Module[] Modules {
			get;
		}

		SingleSteppingEngine SingleSteppingEngine {
			get; set;
		}
		
		event ChildEventHandler ChildEvent;
	}
}

