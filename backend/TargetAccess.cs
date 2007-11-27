using System;

namespace Mono.Debugger
{
	using Mono.Debugger.Backends;

	[Obsolete("FUCK")]
	public abstract class TargetAccess : TargetMemoryAccess
	{
		internal abstract ThreadManager ThreadManager {
			get;
		}

		internal abstract ProcessServant ProcessServant {
			get;
		}

		public abstract TargetState State {
			get;
		}

		public abstract StackFrame CurrentFrame {
			get;
		}

		public abstract TargetAddress CurrentFrameAddress {
			get;
		}

		public abstract Backtrace CurrentBacktrace {
			get;
		}

		public abstract Backtrace GetBacktrace (Backtrace.Mode mode, int max_frames);

		public abstract int GetInstructionSize (TargetAddress address);

		public abstract AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address);

		public abstract AssemblerMethod DisassembleMethod (Method method);
	}
}
