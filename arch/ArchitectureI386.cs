using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	internal class ArchitectureI386 : IArchitecture
	{
		IInferior inferior;

		public ArchitectureI386 (IInferior inferior)
		{
			this.inferior = inferior;
		}

		public TargetAddress GetCallTarget (TargetAddress address, out int insn_size)
		{
			ITargetMemoryReader reader = inferior.ReadMemory (address, 6);

			byte opcode = reader.ReadByte ();

			if (opcode == 0xe8) {
				int target = reader.ReadInteger ();
				insn_size = 5;
				return address + reader.Offset + target;
			} else if (opcode != 0xff) {
				insn_size = 0;
				return TargetAddress.Null;
			}

			byte address_byte = reader.ReadByte ();
			byte register;
			int disp;

			if (((address_byte & 0x18) == 0x10) && ((address_byte >> 6) == 1)) {
				register = (byte) (address_byte & 0x07);
				disp = reader.ReadByte ();
				insn_size = 3;
			} else if (((address_byte & 0x18) == 0x10) && ((address_byte >> 6) == 2)) {
				register = (byte) (address_byte & 0x07);
				disp = reader.ReadInteger ();
				insn_size = 6;
			} else {
				insn_size = 0;
				return TargetAddress.Null;
			}

#if FALSE
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
#endif

			return TargetAddress.Null;
		}

		public TargetAddress GetTrampoline (TargetAddress location,
						    TargetAddress trampoline_address)
		{
			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			ITargetMemoryReader reader = inferior.ReadMemory (location, 10);

			byte opcode = reader.ReadByte ();
			if (opcode != 0x68)
				return TargetAddress.Null;

			int method_info = reader.ReadInteger ();

			opcode = reader.ReadByte ();
			if (opcode != 0xe9)
				return TargetAddress.Null;

			int call_disp = reader.ReadInteger ();

			if (location + call_disp + 10 != trampoline_address)
				return TargetAddress.Null;

			return new TargetAddress (inferior, method_info);
		}
	}
}
