using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	internal enum I386Register
	{
		EBX		= 0,
		ECX		= 1,
		EDX		= 2,
		ESI		= 3,
		EDI		= 4,
		EBP		= 5,
		EAX		= 6,
		XDS		= 7,
		XES		= 8,
		XFS		= 9,
		XGS		= 10,
		ORIG_EAX	= 11,
		EIP		= 12,
		XCS		= 13,
		EFL		= 14,
		ESP		= 15,
		XSS		= 16,
		COUNT		= 17
	}

	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	internal class ArchitectureI386 : IArchitecture
	{
		ITargetAccess target;
		object global_address_domain;

		public ArchitectureI386 (ITargetAccess target, object global_address_domain)
		{
			this.target = target;
			this.global_address_domain = global_address_domain;
		}

		public bool IsRetInstruction (TargetAddress address)
		{
			return target.ReadByte (address) == 0xc3;
		}

		public TargetAddress GetCallTarget (TargetAddress address, out int insn_size)
		{
			ITargetMemoryReader reader = target.ReadMemory (address, 6);

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

			long addr = target.GetRegister ((int) reg);

			TargetAddress vtable_addr = new TargetAddress (global_address_domain, addr + disp);

			if (dereference_addr)
				return target.ReadGlobalAddress (vtable_addr);
			else
				return vtable_addr;
		}

		public TargetAddress GetTrampoline (TargetAddress location,
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

			return new TargetAddress (global_address_domain, method_info);
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

		int[] all_regs = { (int) I386Register.EBX,
				   (int) I386Register.ECX,
				   (int) I386Register.EDX,
				   (int) I386Register.ESI,
				   (int) I386Register.EDI,
				   (int) I386Register.EBP,
				   (int) I386Register.EAX,
				   (int) I386Register.XDS,
				   (int) I386Register.XES,
				   (int) I386Register.XFS,
				   (int) I386Register.XGS,
				   (int) I386Register.ORIG_EAX,
				   (int) I386Register.EIP,
				   (int) I386Register.XCS,
				   (int) I386Register.EFL,
				   (int) I386Register.ESP,
				   (int) I386Register.XSS };

		int[] important_regs = { (int) I386Register.EAX,
					 (int) I386Register.EBX,
					 (int) I386Register.ECX,
					 (int) I386Register.EDX,
					 (int) I386Register.ESI,
					 (int) I386Register.EDI,
					 (int) I386Register.EBP,
					 (int) I386Register.ESP,
					 (int) I386Register.EIP,
					 (int) I386Register.EFL };
				
		string[] registers = { "ebx", "ecx", "edx", "esi", "edi", "ebp", "eax", "xds",
				       "xes", "xfs", "xgs", "orig_eax", "eip", "xcs", "eflags",
				       "esp", "xss" };

		public string PrintRegister (int register, long value)
		{
			switch ((I386Register) register) {
			case I386Register.EFL: {
				ArrayList flags = new ArrayList ();
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
				return String.Format ("{0:x}", value);
			}
		}

		public int GetBreakpointTrampolineData (out TargetAddress method, out TargetAddress code,
							out TargetAddress retaddr)
		{
			TargetAddress stack = new TargetAddress (
				target, target.GetRegister ((int) I386Register.ESP));

			method = target.ReadGlobalAddress (stack);
			code = target.ReadGlobalAddress (stack + target.TargetAddressSize +
							 target.TargetIntegerSize);
			retaddr = target.ReadGlobalAddress (stack + 2 * target.TargetAddressSize +
							    target.TargetIntegerSize);

			return target.ReadInteger (stack + target.TargetAddressSize);
		}

		public int MaxPrologueSize {
			get { return 50; }
		}

		public object UnwindStack (Register[] registers)
		{
			uint[] retval = new uint [7];

			foreach (Register register in registers) {
				switch (register.Index) {
				case (uint) I386Register.EBP:
					retval [0] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.EAX:
					retval [1] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.EBX:
					retval [2] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.ECX:
					retval [3] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.EDX:
					retval [4] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.ESI:
					retval [5] = (uint) ((long) register.Data);
					break;
				case (uint) I386Register.EDI:
					retval [6] = (uint) ((long) register.Data);
					break;
				}
			}

			return retval;
		}

		public Register[] UnwindStack (byte[] code, ITargetMemoryAccess memory, object last_data,
					       out object new_data)
		{
			int pos = 0;
			int length = code.Length;

			uint[] regs = (uint []) last_data;

			new_data = null;
			if (length == 0)
				return null;

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

			TargetAddress ebp = new TargetAddress (memory, regs [0]);
			regs [0] = (uint) target.ReadInteger (ebp);
			ebp -= target.TargetAddressSize;

			while (pos < length) {
				byte opcode = code [pos++];

				if ((opcode < 0x50) || (opcode > 0x57))
					break;

				switch (opcode) {
				case 0x50: /* eax */
					regs [1] = (uint) target.ReadInteger (ebp);
					break;
				case 0x51: /* ecx */
					regs [3] = (uint) target.ReadInteger (ebp);
					break;
				case 0x52: /* edx */
					regs [4] = (uint) target.ReadInteger (ebp);
					break;
				case 0x53: /* ebx */
					regs [2] = (uint) target.ReadInteger (ebp);
					break;
				case 0x56: /* esi */
					regs [5] = (uint) target.ReadInteger (ebp);
					break;
				case 0x57: /* edi */
					regs [6] = (uint) target.ReadInteger (ebp);
					break;
				}

				ebp -= target.TargetIntegerSize;
			}

			new_data = regs;

			Register[] retval = new Register [7];
			retval [0] = new Register ((int) I386Register.EBP, regs [0]);
			retval [1] = new Register ((int) I386Register.EAX, regs [1]);
			retval [2] = new Register ((int) I386Register.EBX, regs [2]);
			retval [3] = new Register ((int) I386Register.ECX, regs [3]);
			retval [4] = new Register ((int) I386Register.EDX, regs [4]);
			retval [5] = new Register ((int) I386Register.ESI, regs [5]);
			retval [6] = new Register ((int) I386Register.EDI, regs [6]);

			return retval;
		}
	}
}
