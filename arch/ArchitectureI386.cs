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
		ITargetLocation trampoline_address;

		public ArchitectureI386 (IInferior inferior)
		{
			this.inferior = inferior;
		}

		public ITargetLocation GenericTrampolineCode {
			get {
				return trampoline_address;
			}

			set {
				trampoline_address = value;
			}
		}

		public ITargetLocation GetCallTarget (ITargetLocation location, out int insn_size)
		{
			ITargetMemoryReader reader = inferior.ReadMemory (location, 6);

			byte opcode = reader.ReadByte ();

			if (opcode == 0xe8) {
				int target = reader.ReadInteger ();
				insn_size = 5;
				return new TargetLocation (location.Address + reader.Offset + target);
			} else if (opcode != 0xff) {
				insn_size = 0;
				return TargetLocation.Null;
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
				return TargetLocation.Null;
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

			return TargetLocation.Null;
		}

		public ITargetLocation GetTrampoline (ITargetLocation location)
		{
			if (trampoline_address == null)
				return null;

			ITargetMemoryReader reader = inferior.ReadMemory (location, 10);

			byte opcode = reader.ReadByte ();
			if (opcode != 0x68)
				return TargetLocation.Null;

			int method_info = reader.ReadInteger ();

			opcode = reader.ReadByte ();
			if (opcode != 0xe9)
				return TargetLocation.Null;

			int call_disp = reader.ReadInteger ();

			long address = location.Address + call_disp + 10;
			if (address != trampoline_address.Address)
				return TargetLocation.Null;

			return new TargetLocation (method_info);
		}
	}
}
