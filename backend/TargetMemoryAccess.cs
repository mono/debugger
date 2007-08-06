using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public abstract class TargetMemoryAccess : DebuggerMarshalByRefObject
	{
		public abstract TargetInfo TargetInfo {
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

		internal abstract int InsertBreakpoint (BreakpointHandle breakpoint,
							TargetAddress address);

		internal abstract void RemoveBreakpoint (int index);
	}
}
