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

	public interface ITargetMemoryReader : ITargetInfo
	{
		// <summary>
		//   Position in the underlying memory stream.
		// </summary>
		long Offset {
			get; set;
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
	}

	public interface ITargetMemoryAccess : ITargetInfo
	{
		byte ReadByte (TargetAddress address);

		int ReadInteger (TargetAddress address);

		long ReadLongInteger (TargetAddress address);

		TargetAddress ReadAddress (TargetAddress address);

		string ReadString (TargetAddress address);

		ITargetMemoryReader ReadMemory (TargetAddress address, int size);

		byte[] ReadBuffer (TargetAddress address, int size);

		Stream GetMemoryStream (TargetAddress address);

		bool CanWrite {
			get;
		}

		void WriteBuffer (TargetAddress address, byte[] buffer, int size);

		void WriteByte (TargetAddress address, byte value);

		void WriteInteger (TargetAddress address, int value);

		void WriteLongInteger (TargetAddress address, long value);

		void WriteAddress (TargetAddress address, TargetAddress value);
	}
}
