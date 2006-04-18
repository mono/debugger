using System;

namespace Mono.Debugger.Backends
{
	public abstract class TargetAccess : MarshalByRefObject, ITargetMemoryAccess
	{
		internal abstract ThreadManager ThreadManager {
			get;
		}

		public abstract Process Process {
			get;
		}

		public abstract ITargetInfo TargetInfo {
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

		public abstract Backtrace GetBacktrace (int max_frames);

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
		// ITargetMemoryInfo
		//

		public abstract Architecture Architecture {
			get;
		}

		public abstract AddressDomain AddressDomain {
			get;
		}

		//
		// ITargetMemoryAccess
		//

		public abstract byte ReadByte (TargetAddress address);

		public abstract int ReadInteger (TargetAddress address);

		public abstract long ReadLongInteger (TargetAddress address);

		public abstract TargetAddress ReadAddress (TargetAddress address);

		public abstract string ReadString (TargetAddress address);

		public abstract TargetBlob ReadMemory (TargetAddress address, int size);

		public abstract byte[] ReadBuffer (TargetAddress address, int size);

		public abstract Registers GetRegisters ();

		public abstract bool CanWrite {
			get;
		}

		public abstract void WriteBuffer (TargetAddress address, byte[] buffer);

		public abstract void WriteByte (TargetAddress address, byte value);

		public abstract void WriteInteger (TargetAddress address, int value);

		public abstract void WriteLongInteger (TargetAddress address, long value);

		public abstract void WriteAddress (TargetAddress address, TargetAddress value);

		public abstract void SetRegisters (Registers registers);

		public abstract int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address);
	}
}
