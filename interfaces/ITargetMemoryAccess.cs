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

		bool CanWrite {
			get;
		}

		void WriteBuffer (TargetAddress address, byte[] buffer);

		void WriteByte (TargetAddress address, byte value);

		void WriteInteger (TargetAddress address, int value);

		void WriteLongInteger (TargetAddress address, long value);

		void WriteAddress (TargetAddress address, TargetAddress value);

		void SetRegisters (Registers registers);

		int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address);
	}
}
