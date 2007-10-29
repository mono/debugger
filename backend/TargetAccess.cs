using System;

namespace Mono.Debugger
{
	using Mono.Debugger.Backends;

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

		//
		// ITargetInfo
		//

		public abstract int TargetIntegerSize {
			get;
		}

		public abstract int TargetLongIntegerSize {
			get;
		}

		public abstract int TargetAddressSize {
			get;
		}

		public abstract bool IsBigEndian {
			get;
		}

		//
		// TargetMemoryAccess
		//

		public abstract bool CanWrite {
			get;
		}

		public abstract void WriteBuffer (TargetAddress address, byte[] buffer);

		public abstract void WriteByte (TargetAddress address, byte value);

		public abstract void WriteInteger (TargetAddress address, int value);

		public abstract void WriteLongInteger (TargetAddress address, long value);

		public abstract void WriteAddress (TargetAddress address, TargetAddress value);

		public abstract void SetRegisters (Registers registers);
	}
}
