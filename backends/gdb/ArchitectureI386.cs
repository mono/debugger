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

		public long GetTrampoline (IDebuggerBackend backend, long address, long generic_trampoline)
		{
			long ptr = address;

			byte opcode = backend.ReadByte (ptr++);
			if (opcode != 0x68)
				return 0;

			uint method_info = backend.ReadInteger (ptr);
			ptr += backend.TargetIntegerSize;

			opcode = backend.ReadByte (ptr++);
			if (opcode != 0xe9)
				return 0;

			int target_addr = backend.ReadSignedInteger (ptr);
			ptr += backend.TargetIntegerSize;

			if (ptr + target_addr != generic_trampoline)
				return 0;

			return method_info;
		}
	}
}
