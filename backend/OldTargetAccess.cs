using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public abstract class OldTargetMemoryAccess : DebuggerMarshalByRefObject
	{
		public abstract TargetMemoryInfo TargetMemoryInfo {
			get;
		}

		public abstract AddressDomain AddressDomain {
			get;
		}

		internal abstract Architecture Architecture {
			get;
		}

		public abstract byte ReadByte (TargetAddress address);

		public abstract int ReadInteger (TargetAddress address);

		public abstract long ReadLongInteger (TargetAddress address);

		public abstract TargetAddress ReadAddress (TargetAddress address);

		public abstract string ReadString (TargetAddress address);

		public abstract TargetBlob ReadMemory (TargetAddress address, int size);

		public abstract byte[] ReadBuffer (TargetAddress address, int size);

		public abstract Registers GetRegisters ();

		internal abstract void InsertBreakpoint (BreakpointHandle breakpoint,
							 TargetAddress address, int domain);

		internal abstract void RemoveBreakpoint (BreakpointHandle handle);
	}

	public abstract class OldTargetAccess : OldTargetMemoryAccess
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
