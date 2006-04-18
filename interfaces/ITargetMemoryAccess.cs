using System;
using System.IO;

namespace Mono.Debugger
{
	internal interface ITargetMemoryAccess
	{
		TargetInfo TargetInfo {
			get;
		}

		Architecture Architecture {
			get;
		}

		byte ReadByte (TargetAddress address);

		int ReadInteger (TargetAddress address);

		long ReadLongInteger (TargetAddress address);

		TargetAddress ReadAddress (TargetAddress address);

		string ReadString (TargetAddress address);

		TargetBlob ReadMemory (TargetAddress address, int size);

		byte[] ReadBuffer (TargetAddress address, int size);

		Registers GetRegisters ();

		int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address);
	}
}
