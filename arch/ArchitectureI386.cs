using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// Keep in sync with DebuggerI386Registers in backends/server/i386-arch.h.
	internal enum I386Register
	{
		EBX		= 0,
		ECX		= 1,
		EDX		= 2,
		ESI		= 3,
		EDI		= 4,
		EBP		= 5,
		EAX		= 6,
		DS		= 7,
		ES		= 8,
		FS		= 9,
		GS		= 10,
		EIP		= 11,
		CS		= 12,
		EFLAGS		= 13,
		ESP		= 14,
		SS		= 15,
		COUNT		= 16
	}

	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	internal class ArchitectureI386 : IArchitecture
	{
		public bool IsRetInstruction (ITargetMemoryAccess memory, TargetAddress address)
		{
			return memory.ReadByte (address) == 0xc3;
		}

		public TargetAddress GetCallTarget (ITargetMemoryAccess target, TargetAddress address, out int insn_size)
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

			I386Register reg;
			switch (register) {
			case 0:
				reg = I386Register.EAX;
				break;

			case 1:
				reg = I386Register.ECX;
				break;

			case 2:
				reg = I386Register.EDX;
				break;

			case 3:
				reg = I386Register.EBX;
				break;

			case 6:
				reg = I386Register.ESI;
				break;

			case 7:
				reg = I386Register.EDI;
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

		public TargetAddress GetTrampoline (ITargetMemoryAccess target,
						    TargetAddress location,
						    TargetAddress trampoline_address)
		{
			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			ITargetMemoryReader reader = target.ReadMemory (location, 10);

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

		public int CountRegisters {
			get {
				return (int) I386Register.COUNT;
			}
		}

		int[] all_regs = { (int) I386Register.EBX,
				   (int) I386Register.ECX,
				   (int) I386Register.EDX,
				   (int) I386Register.ESI,
				   (int) I386Register.EDI,
				   (int) I386Register.EBP,
				   (int) I386Register.EAX,
				   (int) I386Register.DS,
				   (int) I386Register.ES,
				   (int) I386Register.FS,
				   (int) I386Register.GS,
				   (int) I386Register.EIP,
				   (int) I386Register.CS,
				   (int) I386Register.EFLAGS,
				   (int) I386Register.ESP,
				   (int) I386Register.SS };

		int[] important_regs = { (int) I386Register.EAX,
					 (int) I386Register.EBX,
					 (int) I386Register.ECX,
					 (int) I386Register.EDX,
					 (int) I386Register.ESI,
					 (int) I386Register.EDI,
					 (int) I386Register.EBP,
					 (int) I386Register.ESP,
					 (int) I386Register.EIP,
					 (int) I386Register.EFLAGS };
				
		string[] registers = { "ebx", "ecx", "edx", "esi", "edi", "ebp", "eax", "ds",
				       "es", "fs", "gs", "eip", "cs", "eflags", "esp", "ss" };

		int[] reg_sizes = { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };

		public string PrintRegister (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			switch ((I386Register) register.Index) {
			case I386Register.EFLAGS: {
				ArrayList flags = new ArrayList ();
				long value = register.Value;
				if ((value & (1 << 0)) != 0)
					flags.Add ("CF");
				if ((value & (1 << 2)) != 0)
					flags.Add ("PF");
				if ((value & (1 << 4)) != 0)
					flags.Add ("AF");
				if ((value & (1 << 6)) != 0)
					flags.Add ("ZF");
				if ((value & (1 << 7)) != 0)
					flags.Add ("SF");
				if ((value & (1 << 8)) != 0)
					flags.Add ("TF");
				if ((value & (1 << 9)) != 0)
					flags.Add ("IF");
				if ((value & (1 << 10)) != 0)
					flags.Add ("DF");
				if ((value & (1 << 11)) != 0)
					flags.Add ("OF");
				if ((value & (1 << 14)) != 0)
					flags.Add ("NT");
				if ((value & (1 << 16)) != 0)
					flags.Add ("RF");
				if ((value & (1 << 17)) != 0)
					flags.Add ("VM");
				if ((value & (1 << 18)) != 0)
					flags.Add ("AC");
				if ((value & (1 << 19)) != 0)
					flags.Add ("VIF");
				if ((value & (1 << 20)) != 0)
					flags.Add ("VIP");
				if ((value & (1 << 21)) != 0)
					flags.Add ("ID");
				string[] fstrings = new string [flags.Count];
				flags.CopyTo (fstrings, 0);
				return String.Join (" ", fstrings);
			}

			default:
				return String.Format ("{0:x}", register.Value);
			}
		}

		string format (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			int bits = 8;
			string saddr = register.Value.ToString ("x");
			for (int i = saddr.Length; i < bits; i++)
				saddr = "0" + saddr;
			return saddr;
		}

		public string PrintRegisters (StackFrame frame)
		{
			Registers registers = frame.Registers;
			return String.Format (
				"EAX={0}  EBX={1}  ECX={2}  EDX={3}  ESI={4}  EDI={5}\n" +
				"EBP={6}  ESP={7}  EIP={8}  EFLAGS={9}\n",
				format (registers [(int) I386Register.EAX]),
				format (registers [(int) I386Register.EBX]),
				format (registers [(int) I386Register.ECX]),
				format (registers [(int) I386Register.EDX]),
				format (registers [(int) I386Register.ESI]),
				format (registers [(int) I386Register.EDI]),
				format (registers [(int) I386Register.EBP]),
				format (registers [(int) I386Register.ESP]),
				format (registers [(int) I386Register.EIP]),
				PrintRegister (registers [(int) I386Register.EFLAGS]));
		}

		public int MaxPrologueSize {
			get { return 50; }
		}

		public StackFrame UnwindStack (StackFrame frame, ITargetMemoryAccess memory,
					       ISymbolTable symtab, byte[] code)
		{
			int pos = 0;
			int length;

			length = code != null ? code.Length : 0;
			if (length > 0) {
				if ((code [pos] == 0x90) || (code [pos] == 0xcc))
					pos++;

				if (pos+2 >= length)
					return null;
				if (code [pos++] != 0x55)
					return null;
				if (((code [pos] != 0x8b) || (code [pos+1] != 0xec)) &&
				    ((code [pos] != 0x89) || (code [pos+1] != 0xe5)))
					return null;
				pos += 2;
			}

			Registers old_regs = frame.Registers;
			Registers regs = new Registers (this);

			TargetAddress ebp = new TargetAddress (
				memory.AddressDomain, old_regs [(int) I386Register.EBP]);

			int addr_size = memory.TargetAddressSize;
			TargetAddress new_ebp = memory.ReadAddress (ebp);
			regs [(int) I386Register.EBP].SetValue (ebp, new_ebp.Address);

			TargetAddress new_eip = memory.ReadGlobalAddress (ebp + addr_size);
			regs [(int) I386Register.EIP].SetValue (
				ebp + addr_size, new_eip.Address);

			TargetAddress new_esp = ebp + 2 * addr_size;
			regs [(int) I386Register.ESP].SetValue (ebp, new_esp.Address);

			ebp -= addr_size;

			while (pos < length) {
				byte opcode = code [pos++];

				if ((opcode < 0x50) || (opcode > 0x57))
					break;

				long value;
				switch (opcode) {
				case 0x50: /* eax */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.EAX].SetValue (ebp, value);
					break;
				case 0x51: /* ecx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.ECX].SetValue (ebp, value);
					break;
				case 0x52: /* edx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.EDX].SetValue (ebp, value);
					break;
				case 0x53: /* ebx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.EBX].SetValue (ebp, value);
					break;
				case 0x56: /* esi */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.ESI].SetValue (ebp, value);
					break;
				case 0x57: /* edi */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) I386Register.EDI].SetValue (ebp, value);
					break;
				}

				ebp -= addr_size;
			}

			Inferior.StackFrame iframe = new Inferior.StackFrame (
				new_eip, new_esp, new_ebp);

			IMethod new_method = symtab.Lookup (new_eip);
			return StackFrame.CreateFrame (
				frame.Process, iframe, regs, frame.Level + 1, new_method);
		}
	}
}
