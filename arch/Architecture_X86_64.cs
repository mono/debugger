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
	internal class Architecture_X86_64 : MarshalByRefObject, IArchitecture
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

			TargetBinaryReader reader = target.ReadMemory (address, 6).GetReader ();

			byte opcode = reader.ReadByte ();
			byte original_opcode = opcode;

			if ((opcode == 0x48) || (opcode == 0x49))
				opcode = reader.ReadByte ();

			if (opcode == 0xe8) {
				int call_target = reader.ReadInt32 ();
				insn_size = 5;
				return address + reader.Position + call_target;
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
				disp = reader.ReadInt32 ();
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
			if (original_opcode == 0x49) {
				switch (register) {
				case 0: /* r8 */
					reg = X86_64_Register.R8;
					break;
				case 1: /* r9 */
					reg = X86_64_Register.R9;
					break;
				case 2: /* r10 */
					reg = X86_64_Register.R10;
					break;
				case 3: /* r11 */
					reg = X86_64_Register.R11;
					break;
				case 4: /* r12 */
					reg = X86_64_Register.R12;
					break;
				case 5: /* r13 */
					reg = X86_64_Register.R13;
					break;
				case 6: /* r14 */
					reg = X86_64_Register.R14;
					break;
				case 7: /* r15 */
					reg = X86_64_Register.R15;
					break;
				default:
					throw new InvalidOperationException ();
				}
			} else {
				switch (register) {
				case 0: /* rax */
					reg = X86_64_Register.RAX;
					break;
				case 1: /* rcx */
					reg = X86_64_Register.RCX;
					break;
				case 2: /* rdx */
					reg = X86_64_Register.RDX;
					break;
				case 3: /* rbx */
					reg = X86_64_Register.RBX;
					break;
				case 6: /* rsi */
					reg = X86_64_Register.RSI;
					break;
				case 7: /* rdi */
					reg = X86_64_Register.RDI;
					break;
				default:
					throw new InvalidOperationException ();
				}
			}

			if ((original_opcode == 0x48) || (original_opcode == 0x49))
				insn_size++;

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
			TargetBinaryReader reader = target.ReadMemory (address, 10).GetReader ();

			byte opcode = reader.ReadByte ();
			byte opcode2 = reader.ReadByte ();

			if ((opcode == 0xff) && (opcode2 == 0x25)) {
				insn_size = 6;
				int offset = reader.ReadInt32 ();
				return address + offset + 6;
			} else if ((opcode == 0xff) && (opcode2 == 0xa3)) {
				int offset = reader.ReadInt32 ();
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
			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			TargetBinaryReader reader = target.ReadMemory (location, 19).GetReader ();

			reader.Position = 9;

			byte opcode = reader.ReadByte ();
			if (opcode != 0x68)
				return TargetAddress.Null;

			int method_info = reader.ReadInt32 ();

			opcode = reader.ReadByte ();
			if (opcode != 0xe9)
				return TargetAddress.Null;

			int call_disp = reader.ReadInt32 ();

			if (location + call_disp + 19 != trampoline_address)
				return TargetAddress.Null;

			return new TargetAddress (target.GlobalAddressDomain, method_info);
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
				       (int) X86_64_Register.RSI, (int) X86_64_Register.RDI,
				       (int) X86_64_Register.R8, (int) X86_64_Register.R9,
				       (int) X86_64_Register.R10, (int) X86_64_Register.R11,
				       (int) X86_64_Register.R12, (int) X86_64_Register.R13,
				       (int) X86_64_Register.R14, (int) X86_64_Register.R15,
				       (int) X86_64_Register.RIP };

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

		SimpleStackFrame unwind_method (SimpleStackFrame frame,
						ITargetMemoryAccess memory, byte[] code,
						int pos)
		{
			Registers old_regs = frame.Registers;
			Registers regs = new Registers (old_regs);

			TargetAddress rbp = new TargetAddress (
				memory.AddressDomain, old_regs [(int) X86_64_Register.RBP]);

			int addr_size = memory.TargetAddressSize;
			TargetAddress new_rbp = memory.ReadAddress (rbp);
			regs [(int) X86_64_Register.RBP].SetValue (rbp, new_rbp);

			TargetAddress new_rip = memory.ReadGlobalAddress (rbp + addr_size);
			regs [(int) X86_64_Register.RIP].SetValue (rbp + addr_size, new_rip);

			TargetAddress new_rsp = rbp + 2 * addr_size;
			regs [(int) X86_64_Register.RSP].SetValue (rbp, new_rsp);

			regs [(int) X86_64_Register.RSI].Valid = true;
			regs [(int) X86_64_Register.RDI].Valid = true;
			regs [(int) X86_64_Register.R12].Valid = true;
			regs [(int) X86_64_Register.R13].Valid = true;
			regs [(int) X86_64_Register.R14].Valid = true;
			regs [(int) X86_64_Register.R15].Valid = true;

			rbp -= addr_size;

			int length = code.Length;
			while (pos < length) {
				byte opcode = code [pos++];

				long value;
				if ((opcode == 0x41) && (pos < length)) {
					byte opcode2 = code [pos++];

					if ((opcode2 < 0x50) || (opcode2 > 0x57))
						break;

					switch (opcode2) {
					case 0x50: /* r8 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R8].SetValue (rbp, value);
						break;
					case 0x51: /* r9 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R9].SetValue (rbp, value);
						break;
					case 0x52: /* r10 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R10].SetValue (rbp, value);
						break;
					case 0x53: /* r11 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R11].SetValue (rbp, value);
						break;
					case 0x54: /* r12 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R12].SetValue (rbp, value);
						break;
					case 0x55: /* r13 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R13].SetValue (rbp, value);
						break;
					case 0x56: /* r14 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R14].SetValue (rbp, value);
						break;
					case 0x57: /* r15 */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.R15].SetValue (rbp, value);
						break;
					}
				} else {
					if ((opcode < 0x50) || (opcode > 0x57))
						break;

					switch (opcode) {
					case 0x50: /* rax */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RAX].SetValue (rbp, value);
						break;
					case 0x51: /* rcx */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RCX].SetValue (rbp, value);
						break;
					case 0x52: /* rdx */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RDX].SetValue (rbp, value);
						break;
					case 0x53: /* rbx */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RBX].SetValue (rbp, value);
						break;
					case 0x56: /* rsi */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RSI].SetValue (rbp, value);
						break;
					case 0x57: /* rdi */
						value = (long) memory.ReadInteger (rbp);
						regs [(int) X86_64_Register.RDI].SetValue (rbp, value);
						break;
					}
				}

				rbp -= addr_size;
			}

			return new SimpleStackFrame (
				new_rip, new_rsp, new_rbp, regs, frame.Level + 1);
		}

		SimpleStackFrame read_prologue (SimpleStackFrame frame,
						ITargetMemoryAccess memory, byte[] code)
		{
			int length = code.Length;
			int pos = 0;

			if (length < 4)
				return null;

			while ((pos < length) &&
			       (code [pos] == 0x90) || (code [pos] == 0xcc))
				pos++;

			if ((pos+5 < length) && (code [pos] == 0x55) && (code [pos+1] == 0x48) &&
			    (((code [pos+2] == 0x8b) && (code [pos+3] == 0xec)) ||
			     ((code [pos+2] == 0x89) && (code [pos+3] == 0xe5)))) {
				pos += 4;
				return unwind_method (frame, memory, code, pos);
			}

			//
			// Try smart unwinding
			//

			Console.WriteLine ("TRY SMART UNWIND: {0} {1} {2}",
					   frame.Address, frame.StackPointer,
					   frame.FrameAddress);

			return null;
		}

		public SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
						     SimpleStackFrame frame, Symbol name,
						     byte[] code)
		{
			if ((code != null) && (code.Length > 4))
				return read_prologue (frame, memory, code);

			TargetAddress rbp = frame.FrameAddress;

			int addr_size = memory.TargetAddressSize;

			Registers regs = new Registers (this);

			TargetAddress new_rbp = memory.ReadAddress (rbp);
			regs [(int) X86_64_Register.RBP].SetValue (rbp, new_rbp);

			TargetAddress new_rip = memory.ReadGlobalAddress (rbp + addr_size);
			regs [(int) X86_64_Register.RIP].SetValue (rbp + addr_size, new_rip);

			TargetAddress new_rsp = rbp + 2 * addr_size;
			regs [(int) X86_64_Register.RSP].SetValue (rbp, new_rsp);

			rbp -= addr_size;

			return new SimpleStackFrame (
				new_rip, new_rsp, new_rbp, regs, frame.Level + 1);
		}

		public SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
						     TargetAddress stack, TargetAddress frame)
		{
			TargetAddress rip = memory.ReadGlobalAddress (stack);
			TargetAddress rsp = stack;
			TargetAddress rbp = frame;

			Registers regs = new Registers (this);
			regs [(int) X86_64_Register.RIP].SetValue (rip);
			regs [(int) X86_64_Register.RSP].SetValue (rsp);
			regs [(int) X86_64_Register.RBP].SetValue (rbp);

			return new SimpleStackFrame (rip, rsp, rbp, regs, 0);
		}
	}
}
