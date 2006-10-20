using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetMemoryAccess
	{
		byte ReadByte (TargetAddress address);

		int ReadInteger (TargetAddress address);

		long ReadLongInteger (TargetAddress address);

		TargetAddress ReadAddress (TargetAddress address);

		string ReadString (TargetAddress address);

		byte[] ReadBuffer (TargetAddress address, int size);

		IRegisters GetRegisters ();
	}
}
