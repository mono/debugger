using System;

namespace Mono.Debugger
{
	// <summary>
	//   This can either be an executable process or a core file.
	// </summary>
	public interface IProcess : IDisposable
	{
		// <summary>
		//   If true, we have a target.
		// </summary>
		bool HasTarget {
			get;
		}

		// <summary>
		//   If true, we have a target which can be executed (ie. it's not a core file).
		// </summary>
		bool CanRun {
			get;
		}

		// <summary>
		//   If true, we have a target which can be executed and it is currently stopped
		//   so that we can issue a step command.
		// </summary>
		bool CanStep {
			get;
		}

		// <summary>
		//   If true, the target is currently stopped and thus its memory/registers can
		//   be read/writtern.
		// </summary>
		bool IsStopped {
			get;
		}

		TargetState State {
			get;
		}

		void Kill ();

		IArchitecture Architecture {
			get;
		}

		ITargetMemoryInfo TargetMemoryInfo {
			get;
		}

		ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		ITargetAccess TargetAccess {
			get;
		}

		IDisassembler Disassembler {
			get;
		}

		TargetAddress CurrentFrameAddress {
			get;
		}

		StackFrame CurrentFrame {
			get;
		}

		TargetMemoryArea[] GetMemoryMaps ();

		Backtrace GetBacktrace ();

		Register[] GetRegisters ();
	}
}
