using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	public class ArchitectureI386 : IArchitecture
	{
		public long GetCallTarget (IDebuggerBackend backend, long address, out int insn_size)
		{
			byte opcode = backend.ReadByte (address);

			if (opcode == 0xe8) {
				int target = backend.ReadSignedInteger (address + 1);
				insn_size = 5;
				return address + target + 5;
			}

			insn_size = 0;

			return 0;
		}
	}
}
