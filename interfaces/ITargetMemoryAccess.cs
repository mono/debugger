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
		//   Get the underlying BinaryReader.
		// </summary>
		BinaryReader BinaryReader {
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
		ITargetLocation ReadAddress ();
	}

	public interface ITargetMemoryAccess : ITargetInfo
	{
		byte ReadByte (ITargetLocation location);

		int ReadInteger (ITargetLocation location);

		long ReadLongInteger (ITargetLocation location);

		ITargetLocation ReadAddress (ITargetLocation location);

		string ReadString (ITargetLocation location);

		ITargetMemoryReader ReadMemory (ITargetLocation location, int size);

		byte[] ReadBuffer (ITargetLocation location, long offset, int size);

		Stream GetMemoryStream (ITargetLocation location);

		bool CanWrite {
			get;
		}

		void WriteBuffer (ITargetLocation location, byte[] buffer, long offset, int size);

		void WriteByte (ITargetLocation location, byte value);

		void WriteInteger (ITargetLocation location, int value);

		void WriteLongInteger (ITargetLocation location, long value);

		void WriteAddress (ITargetLocation location, ITargetLocation address);
	}
}
