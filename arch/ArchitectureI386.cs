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
		XSS		= 16
	}

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

		public bool IsRetInstruction (TargetAddress address)
		{
			return inferior.ReadByte (address) == 0xc3;
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

			long addr = inferior.GetRegister ((int) reg);

			TargetAddress vtable_addr = new TargetAddress (inferior, addr + disp);

			if (dereference_addr)
				return inferior.ReadAddress (vtable_addr);
			else
				return vtable_addr;
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
				inferior, inferior.GetRegister ((int) I386Register.ESP));

			method = inferior.ReadAddress (stack);
			code = inferior.ReadAddress (stack + inferior.TargetAddressSize +
						     inferior.TargetIntegerSize);
			retaddr = inferior.ReadAddress (stack + 2 * inferior.TargetAddressSize +
							inferior.TargetIntegerSize);

			return inferior.ReadInteger (stack + inferior.TargetAddressSize);
		}
	}
}
