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
	}

	public interface ITargetMemoryReader : ITargetMemoryInfo
	{
		// <summary>
		//   Position in the underlying memory stream.
		// </summary>
		long Offset {
			get; set;
		}

		// <summary>
		//   Total size of this memory stream.
		// </summary>
		long Size {
			get;
		}

		// <summary>
		//   The full contents of this memory stream.
		// </summary>
		byte[] Contents {
			get;
		}

		// <summary>
		//   Get the underlying TargetBinaryReader.
		// </summary>
		TargetBinaryReader BinaryReader {
			get;
		}

		// <summary>
		//   Read a single byte from the target's address space at address @address.
		// </summary>
		byte ReadByte ();

		// <summary>
		//   Read an integer from the target's address space at address @address.
		// </summary>
		int ReadInteger ();

		// <summary>
		//   Read a long int from the target's address space at address @address.
		// </summary>
		long ReadLongInteger ();

		// <summary>
		//   Read an address from the target's address space at address @address.
		// </summary>
		TargetAddress ReadAddress ();

		TargetAddress ReadGlobalAddress ();
	}

	public interface ITargetMemoryInfo : ITargetInfo
	{
		AddressDomain AddressDomain {
			get;
		}

		AddressDomain GlobalAddressDomain {
			get;
		}
	}

	public interface ITargetMemoryAccess : ITargetMemoryInfo
	{
		byte ReadByte (TargetAddress address);

		int ReadInteger (TargetAddress address);

		long ReadLongInteger (TargetAddress address);

		TargetAddress ReadAddress (TargetAddress address);

		TargetAddress ReadGlobalAddress (TargetAddress address);

		string ReadString (TargetAddress address);

		ITargetMemoryReader ReadMemory (TargetAddress address, int size);

		ITargetMemoryReader ReadMemory (byte[] buffer);

		byte[] ReadBuffer (TargetAddress address, int size);

		bool CanWrite {
			get;
		}

		void WriteBuffer (TargetAddress address, byte[] buffer);

		void WriteByte (TargetAddress address, byte value);

		void WriteInteger (TargetAddress address, int value);

		void WriteLongInteger (TargetAddress address, long value);

		void WriteAddress (TargetAddress address, TargetAddress value);
	}

	public interface ITargetAccess : ITargetMemoryAccess
	{
		long GetRegister (int register);

		TargetAddress CallMethod (TargetAddress method, string argument);
		TargetAddress CallMethod (TargetAddress method, TargetAddress argument1, TargetAddress argument2);
	}
}
