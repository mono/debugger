using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public abstract class TargetMemoryAccess : DebuggerMarshalByRefObject
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
