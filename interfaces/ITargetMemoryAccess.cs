using System;
using System.IO;

namespace Mono.Debugger
{
	public interface ITargetInfo
	{
		// <summary>
		//   Size of an address in the target.
		// </summary>
		int TargetAddressSize {
			get;
		}

		// <summary>
		//   Size of an integer in the target.
		// </summary>
		int TargetIntegerSize {
			get;
		}

		// <summary>
		//   Size of a long integer in the target.
		// </summary>
		int TargetLongIntegerSize {
			get;
		}

		// <summary>
		//   Whether this architecture is big-endian.
		// </summary>
		bool IsBigEndian {
			get;
		}

		AddressDomain AddressDomain {
			get;
		}
	}

	internal interface ITargetMemoryAccess : ITargetInfo
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
