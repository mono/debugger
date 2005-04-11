using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// Keep in sync with DebuggerRegisters in backends/server/x86-arch.h.
	internal enum X86_64_Register
	{
		R15		= 0,
		R14,
		R13,
		R12,
		RBP,
		RBX,
		R11,
		R10,
		R9,
		R8,
		RAX,
		RCX,
		RDX,
		RSI,
		RDI,
		ORIG_RAX,
		RIP,
		CS,
		EFLAGS,
		RSP,
		SS,
		FS_BASE,
		GS_BASE,
		DS,
		ES,
		GS,

		COUNT
	}

	// <summary>
	//   Architecture-dependent stuff for the x86_64.
	// </summary>
	internal class Architecture_X86_64 : IArchitecture
	{
		public bool IsRetInstruction (ITargetMemoryAccess memory, TargetAddress address)
		{
			return memory.ReadByte (address) == 0xc3;
		}

		public TargetAddress GetCallTarget (ITargetMemoryAccess target, TargetAddress address,
						    out int insn_size)
		{
			if (address.Address == 0xffffe002) {
				insn_size = 0;
				return TargetAddress.Null;
			}

			ITargetMemoryReader reader = target.ReadMemory (address, 6);

			byte opcode = reader.ReadByte ();

			if (opcode == 0xe8) {
				int call_target = reader.ReadInteger ();
				insn_size = 5;
				return address + reader.Offset + call_target;
			} else if (opcode != 0xff) {
				insn_size = 0;
				return TargetAddress.Null;
			}

			byte address_byte = reader.ReadByte ();
			byte register;
			int disp;
			bool dereference_addr;

			if (((address_byte & 0x38) == 0x10) && ((address_byte >> 6) == 1)) {
				register = (byte) (address_byte & 0x07);
				disp = reader.ReadByte ();
				insn_size = 3;
				dereference_addr = true;
			} else if (((address_byte & 0x38) == 0x10) && ((address_byte >> 6) == 2)) {
				register = (byte) (address_byte & 0x07);
				disp = reader.ReadInteger ();
				insn_size = 6;
				dereference_addr = true;
			} else if (((address_byte & 0x38) == 0x10) && ((address_byte >> 6) == 3)) {
				register = (byte) (address_byte & 0x07);
				disp = 0;
				insn_size = 2;
				dereference_addr = false;
			} else if (((address_byte & 0x38) == 0x10) && ((address_byte >> 6) == 0)) {
				register = (byte) (address_byte & 0x07);
				disp = 0;
				insn_size = 2;
				dereference_addr = true;
			} else {
				insn_size = 0;
				return TargetAddress.Null;
			}

			X86_64_Register reg;
			switch (register) {
			case 0:
				reg = X86_64_Register.RAX;
				break;

			case 1:
				reg = X86_64_Register.RCX;
				break;

			case 2:
				reg = X86_64_Register.RDX;
				break;

			case 3:
				reg = X86_64_Register.RBX;
				break;

			case 6:
				reg = X86_64_Register.RSI;
				break;

			case 7:
				reg = X86_64_Register.RDI;
				break;

			default:
				insn_size = 0;
				return TargetAddress.Null;
			}

			Registers regs = target.GetRegisters ();
			Register addr = regs [(int) reg];

			TargetAddress vtable_addr = new TargetAddress (target.GlobalAddressDomain, addr);
			vtable_addr += disp;

			if (dereference_addr)
				return target.ReadGlobalAddress (vtable_addr);
			else
				return vtable_addr;
		}

		public TargetAddress GetJumpTarget (ITargetMemoryAccess target, TargetAddress address,
						    out int insn_size)
		{
			ITargetMemoryReader reader = target.ReadMemory (address, 10);

			byte opcode = reader.ReadByte ();
			byte opcode2 = reader.ReadByte ();

			if ((opcode == 0xff) && (opcode2 == 0x25)) {
				insn_size = 6;
				int offset = reader.BinaryReader.ReadInt32 ();
				return address + offset + 6;
			} else if ((opcode == 0xff) && (opcode2 == 0xa3)) {
				int offset = reader.BinaryReader.ReadInt32 ();
				Registers regs = target.GetRegisters ();
				long rbx = regs [(int) X86_64_Register.RBX].Value;

				insn_size = 6;
				return new TargetAddress (target.AddressDomain, rbx + offset);
			}

			insn_size = 0;
			return TargetAddress.Null;
		}

		public TargetAddress GetTrampoline (ITargetMemoryAccess target,
						    TargetAddress location,
						    TargetAddress trampoline_address)
		{
			return TargetAddress.Null;
		}

		public string[] RegisterNames {
			get {
				return registers;
			}
		}

		public int[] RegisterIndices {
			get {
				return important_regs;
			}
		}

		public int[] AllRegisterIndices {
			get {
				return all_regs;
			}
		}

		public int[] RegisterSizes {
			get {
				return reg_sizes;
			}
		}

		public int[] RegisterMap {
			get {
				return register_map;
			}
		}

		public int CountRegisters {
			get {
				return (int) X86_64_Register.COUNT;
			}
		}

		int[] all_regs = { (int) X86_64_Register.R15,
				   (int) X86_64_Register.R14,
				   (int) X86_64_Register.R13,
				   (int) X86_64_Register.R12,
				   (int) X86_64_Register.RBP,
				   (int) X86_64_Register.RBX,
				   (int) X86_64_Register.R11,
				   (int) X86_64_Register.R10,
				   (int) X86_64_Register.R9,
				   (int) X86_64_Register.R8,
				   (int) X86_64_Register.RAX,
				   (int) X86_64_Register.RCX,
				   (int) X86_64_Register.RDX,
				   (int) X86_64_Register.RSI,
				   (int) X86_64_Register.RDI,
				   (int) X86_64_Register.ORIG_RAX,
				   (int) X86_64_Register.RIP,
				   (int) X86_64_Register.CS,
				   (int) X86_64_Register.EFLAGS,
				   (int) X86_64_Register.RSP,
				   (int) X86_64_Register.SS,
				   (int) X86_64_Register.FS_BASE,
				   (int) X86_64_Register.GS_BASE,
				   (int) X86_64_Register.DS,
				   (int) X86_64_Register.ES,
				   (int) X86_64_Register.GS };

		int[] important_regs = { (int) X86_64_Register.RBP,
					 (int) X86_64_Register.RBX,
					 (int) X86_64_Register.RAX,
					 (int) X86_64_Register.RCX,
					 (int) X86_64_Register.RDX,
					 (int) X86_64_Register.RSI,
					 (int) X86_64_Register.RDI,
					 (int) X86_64_Register.RIP,
					 (int) X86_64_Register.EFLAGS,
					 (int) X86_64_Register.RSP };

		// FIXME: Map mono/arch/amd64/amd64-codegen.h registers to
		//        debugger/arch/IArchitecture_X86_64.cs registers.
		int[] register_map = { (int) X86_64_Register.RAX, (int) X86_64_Register.RCX,
				       (int) X86_64_Register.RDX, (int) X86_64_Register.RBX,
				       (int) X86_64_Register.RSP, (int) X86_64_Register.RBP,
				       (int) X86_64_Register.RSI, (int) X86_64_Register.RDI };

		string[] registers = { "r15", "r14", "r13", "r12", "rbp", "rbx", "r11", "r10",
				       "r9", "r8", "rax", "rcx", "rdx", "rsi", "rdi", "orig_rax",
				       "rip", "cs", "eflags", "rsp", "fs_base", "gs_base",
				       "ds", "es", "gs" };

		int[] reg_sizes = { 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
				    8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8 };

		public string PrintRegister (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			return String.Format ("{0:x}", register.Value);
		}

		string format (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			int bits = 16;
			string saddr = register.Value.ToString ("x");
			for (int i = saddr.Length; i < bits; i++)
				saddr = "0" + saddr;
			return saddr;
		}

		public string PrintRegisters (StackFrame frame)
		{
			Registers registers = frame.Registers;
			return String.Format (
				"RAX={0}  RBX={1}  RCX={2}  RDX={3}\n" +
				"RSI={4}  RDI={5}  RBP={6}  RSP={7}\n" +
				"RIP={8}  EFLAGS={9}\n",
				format (registers [(int) X86_64_Register.RAX]),
				format (registers [(int) X86_64_Register.RBX]),
				format (registers [(int) X86_64_Register.RCX]),
				format (registers [(int) X86_64_Register.RDX]),
				format (registers [(int) X86_64_Register.RSI]),
				format (registers [(int) X86_64_Register.RDI]),
				format (registers [(int) X86_64_Register.RBP]),
				format (registers [(int) X86_64_Register.RSP]),
				format (registers [(int) X86_64_Register.RIP]),
				PrintRegister (registers [(int) X86_64_Register.EFLAGS]));
		}

		public int MaxPrologueSize {
			get { return 50; }
		}

		public SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
						     SimpleStackFrame frame, Symbol name,
						     byte[] code)
		{
			return null;
		}

		public SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
						     TargetAddress stack, TargetAddress frame)
		{
			return null;
		}
	}
}
