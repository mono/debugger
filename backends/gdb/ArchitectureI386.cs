using System;
using Mono.Debugger.Backends;

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
			} else if (opcode != 0xff) {
				insn_size = 0;
				return 0;
			}

			byte address_byte = backend.ReadByte (address + 1);
			byte register;
			int disp;

			if (((address_byte & 0x18) == 0x10) && ((address_byte >> 6) == 1)) {
				register = (byte) (address_byte & 0x07);
				disp = backend.ReadByte (address + 2);
				insn_size = 3;
			} else if (((address_byte & 0x18) == 0x10) && ((address_byte >> 6) == 2)) {
				register = (byte) (address_byte & 0x07);
				disp = backend.ReadSignedInteger (address + 2);
				insn_size = 6;
			} else {
				insn_size = 0;
				return 0;
			}

			string regname;
			switch (register) {
			case 0:
				regname = "eax";
				break;

			case 1:
				regname = "ecx";
				break;

			case 2:
				regname = "edx";
				break;

			case 3:
				regname = "ebx";
				break;

			case 6:
				regname = "esi";
				break;

			case 7:
				regname = "edi";
				break;

			default:
				insn_size = 0;
				return 0;
			}

			int addr = backend.ReadIntegerRegister (regname);

			long vtable_addr = addr + disp;

			return backend.ReadAddress (vtable_addr);
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
