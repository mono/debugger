using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
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
	internal class Architecture_I386 : Architecture
	{
		internal Architecture_I386 (ProcessServant process, TargetInfo info)
			: base (process, info)
		{ }

		internal override bool IsRetInstruction (TargetMemoryAccess memory,
							 TargetAddress address)
		{
			return memory.ReadByte (address) == 0xc3;
		}

		internal override bool IsSyscallInstruction (TargetMemoryAccess memory,
							     TargetAddress address)
		{
			try {
				return ((memory.ReadByte (address - 2) == 0x0f) &&
					(memory.ReadByte (address - 1) == 0x05)) ||
					((memory.ReadByte (address - 2) == 0xeb) &&
					 (memory.ReadByte (address - 1) == 0xf3) &&
					 (memory.ReadByte (address - 11) == 0x0f) &&
					 (memory.ReadByte (address - 10) == 0x34));
			} catch {
				return false;
			}
		}

		internal override TargetAddress GetCallTarget (TargetMemoryAccess target,
							       TargetAddress address,
							       out int insn_size)
		{
			if (address.Address == 0xffffe002) {
				insn_size = 0;
				return TargetAddress.Null;
			}

			TargetBinaryReader reader = target.ReadMemory (address, 6).GetReader ();

			byte opcode = reader.ReadByte ();

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

			TargetAddress vtable_addr = new TargetAddress (AddressDomain, addr.Value);
			vtable_addr += disp;

			if (dereference_addr)
				return target.ReadAddress (vtable_addr);
			else
				return vtable_addr;
		}

		internal override TargetAddress GetJumpTarget (TargetMemoryAccess target,
							       TargetAddress address,
							       out int insn_size)
		{
			TargetBinaryReader reader = target.ReadMemory (address, 10).GetReader ();

			byte opcode = reader.ReadByte ();
			byte opcode2 = reader.ReadByte ();

			if ((opcode == 0xff) && (opcode2 == 0x25)) {
				insn_size = 2 + TargetAddressSize;
				return new TargetAddress (AddressDomain, reader.ReadAddress ());
			} else if ((opcode == 0xff) && (opcode2 == 0xa3)) {
				int offset = reader.ReadInt32 ();
				Registers regs = target.GetRegisters ();
				long ebx = regs [(int) I386Register.EBX].Value;

				insn_size = 6;
				return new TargetAddress (AddressDomain, ebx + offset);
			}

			insn_size = 0;
			return TargetAddress.Null;
		}

		internal override TargetAddress GetTrampoline (TargetMemoryAccess target,
							       TargetAddress location,
							       TargetAddress trampoline_address)
		{
			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			TargetBinaryReader reader = target.ReadMemory (location, 10).GetReader ();

			byte opcode = reader.ReadByte ();
			if (opcode != 0x68)
				return TargetAddress.Null;

			int method_info = reader.ReadInt32 ();

			opcode = reader.ReadByte ();
			if (opcode != 0xe9)
				return TargetAddress.Null;

			int call_disp = reader.ReadInt32 ();

			if (location + call_disp + 10 != trampoline_address)
				return TargetAddress.Null;

			return new TargetAddress (AddressDomain, method_info);
		}

		public override string[] RegisterNames {
			get {
				return registers;
			}
		}

		public override int[] RegisterIndices {
			get {
				return important_regs;
			}
		}

		public override int[] AllRegisterIndices {
			get {
				return all_regs;
			}
		}

		public override int[] RegisterSizes {
			get {
				return reg_sizes;
			}
		}

		internal override int[] RegisterMap {
			get {
				return register_map;
			}
		}

		internal override int[] DwarfFrameRegisterMap {
			get {
				return dwarf_frame_register_map;
			}
		}

		internal override int CountRegisters {
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

		// FIXME: Map mono/arch/x86/x86-codegen.h registers to
		//        debugger/arch/IArchitecture_I386.cs registers.
		int[] register_map = { (int) I386Register.EAX, (int) I386Register.ECX,
				       (int) I386Register.EDX, (int) I386Register.EBX,
				       (int) I386Register.ESP, (int) I386Register.EBP,
				       (int) I386Register.ESI, (int) I386Register.EDI };

		int[] dwarf_frame_register_map = new int [] {
			(int) I386Register.EIP, (int) I386Register.ESP, (int) I386Register.EBP,

			(int) I386Register.EAX, (int) I386Register.EBX, (int) I386Register.ECX,
			(int) I386Register.EDX, (int) I386Register.ESP, (int) I386Register.EBP,
			(int) I386Register.ESI, (int) I386Register.EDI
		};
				
		string[] registers = { "ebx", "ecx", "edx", "esi", "edi", "ebp", "eax", "ds",
				       "es", "fs", "gs", "eip", "cs", "eflags", "esp", "ss" };

		int[] reg_sizes = { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };

		public override string PrintRegister (Register register)
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

		public override string PrintRegisters (StackFrame frame)
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

		internal override int MaxPrologueSize {
			get { return 50; }
		}

		StackFrame unwind_method (StackFrame frame, TargetMemoryAccess memory, byte[] code,
					  int pos, int offset)
		{
			Registers old_regs = frame.Registers;
			Registers regs = new Registers (old_regs);

			TargetAddress ebp = new TargetAddress (
				AddressDomain, old_regs [(int) I386Register.EBP].GetValue ());

			int addr_size = TargetAddressSize;
			TargetAddress new_ebp = memory.ReadAddress (ebp);
			regs [(int) I386Register.EBP].SetValue (ebp, new_ebp);

			TargetAddress new_eip = memory.ReadAddress (ebp + addr_size);
			regs [(int) I386Register.EIP].SetValue (ebp + addr_size, new_eip);

			TargetAddress new_esp = ebp + 2 * addr_size;
			regs [(int) I386Register.ESP].SetValue (ebp, new_esp);

			regs [(int) I386Register.ESI].Valid = true;
			regs [(int) I386Register.EDI].Valid = true;

			ebp -= addr_size;

			int length = System.Math.Min (code.Length, offset);
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

			return CreateFrame (frame.Thread, new_eip, new_esp, new_ebp, regs, true);
		}

		StackFrame read_prologue (StackFrame frame, TargetMemoryAccess memory,
					  byte[] code, int offset)
		{
			int length = code.Length;
			int pos = 0;

			if (length < 3)
				return null;

			while ((pos < length) &&
			       (code [pos] == 0x90) || (code [pos] == 0xcc))
				pos++;

			if (pos+4 >= length) {
				// unknown prologue
				return null;
			}

			if (pos >= offset) {
				Registers old_regs = frame.Registers;
				Registers regs = new Registers (old_regs);

				TargetAddress new_eip = memory.ReadAddress (frame.StackPointer);
				regs [(int) I386Register.EIP].SetValue (frame.StackPointer, new_eip);

				TargetAddress new_esp = frame.StackPointer + TargetAddressSize;
				TargetAddress new_ebp = frame.FrameAddress;

				regs [(int) I386Register.ESP].SetValue (new_esp);

				return CreateFrame (frame.Thread, new_eip, new_esp, new_ebp, regs, true);
			}

			// push %ebp
			if (code [pos++] != 0x55)
				return null;

			if (pos >= offset) {
				Registers old_regs = frame.Registers;
				Registers regs = new Registers (old_regs);

				int addr_size = TargetAddressSize;
				TargetAddress new_ebp = memory.ReadAddress (frame.StackPointer);
				regs [(int) I386Register.EBP].SetValue (frame.StackPointer, new_ebp);

				TargetAddress new_esp = frame.StackPointer + addr_size;
				TargetAddress new_eip = memory.ReadAddress (new_esp);
				regs [(int) I386Register.EIP].SetValue (new_esp, new_eip);
				new_esp -= addr_size;

				regs [(int) I386Register.ESP].SetValue (new_esp);

				return CreateFrame (frame.Thread, new_eip, new_esp, new_ebp, regs, true);
			}

			// mov %ebp, %esp
			if (((code [pos] != 0x8b) || (code [pos+1] != 0xec)) &&
			    ((code [pos] != 0x89) || (code [pos+1] != 0xe5))) {
				// unknown prologue
				return null;
			}

			pos += 2;
			if (pos >= offset)
				return null;

			return unwind_method (frame, memory, code, pos, offset);
		}

		internal override Registers CopyRegisters (Registers old_regs)
		{
			Registers regs = new Registers (old_regs);

			regs [(int) I386Register.EBP].Valid = true;
			regs [(int) I386Register.EBX].Valid = true;
			regs [(int) I386Register.ESI].Valid = true;
			regs [(int) I386Register.EDI].Valid = true;

			return regs;
		}

		StackFrame try_pthread_cond_timedwait (StackFrame frame, TargetMemoryAccess memory)
		{
			Symbol name = frame.Name;

			/*
			 * This is a hack for pthread_cond_timedwait() on Red Hat 9.
			 */
			if ((name == null) || (name.Name != "pthread_cond_timedwait") ||
			    (name.Offset != 0xe5))
				return null;

			/*
			 * Disassemble some bytes of the method to find out whether
			 * it's the "correct" one.
			 */
			uint data = (uint) memory.ReadInteger (name.Address);
			if (data != 0x53565755)
				return null;
			data = (uint) memory.ReadInteger (name.Address + 4);
			if (data != 0x14245c8b)
				return null;
			data = (uint) memory.ReadInteger (name.Address + 8);
			if (data != 0x1c246c8b)
				return null;

			data = (uint) memory.ReadInteger (frame.TargetAddress);
			if (data != 0x8910eb83)
				return null;

			data = (uint) memory.ReadInteger (frame.TargetAddress + 0x7b);
			if (data != 0x852cc483)
				return null;
			data = (uint) memory.ReadInteger (frame.TargetAddress + 0x7f);
			if (data != 0xc6440fc0)
				return null;

			TargetAddress esp = frame.StackPointer;

			Registers regs = new Registers (this);

			TargetAddress ebx = memory.ReadAddress (esp + 0x2c);
			TargetAddress esi = memory.ReadAddress (esp + 0x30);
			TargetAddress edi = memory.ReadAddress (esp + 0x34);
			TargetAddress ebp = memory.ReadAddress (esp + 0x38);
			TargetAddress eip = memory.ReadAddress (esp + 0x3c);

			regs [(int)I386Register.EBX].SetValue (esp + 0x2c, ebx);
			regs [(int)I386Register.ESI].SetValue (esp + 0x30, esi);
			regs [(int)I386Register.EDI].SetValue (esp + 0x34, edi);
			regs [(int)I386Register.EBP].SetValue (esp + 0x38, ebp);
			regs [(int)I386Register.EIP].SetValue (esp + 0x3c, eip);

			esp += 0x40;
			regs [(int)I386Register.ESP].SetValue (esp.Address);

			return CreateFrame (frame.Thread, eip, esp, ebp, regs, true);
		}

		StackFrame try_syscall_trampoline (StackFrame frame, TargetMemoryAccess memory)
		{
			/*
			 * This is a hack for system call trampolines on NPTL-enabled glibc's.
			 */
			if (frame.TargetAddress.Address != 0xffffe002)
				return null;

			Registers old_regs = frame.Registers;
			Registers regs = CopyRegisters (old_regs);

			TargetAddress esp = frame.StackPointer;

			TargetAddress new_eip = memory.ReadAddress (esp);
			TargetAddress new_esp = esp + 4;
			TargetAddress new_ebp = frame.FrameAddress;

			regs [(int)I386Register.EIP].SetValue (esp, new_eip);
			regs [(int)I386Register.EBP].SetValue (new_ebp);

			return CreateFrame (frame.Thread, new_eip, new_esp, new_ebp, regs, true);
		}

		StackFrame do_hacks (StackFrame frame, TargetMemoryAccess memory)
		{
			StackFrame new_frame;
			try {
				new_frame = try_pthread_cond_timedwait (frame, memory);
				if (new_frame != null)
					return new_frame;
			} catch {
				new_frame = null;
			}

			try {
				new_frame = try_syscall_trampoline (frame, memory);
				if (new_frame != null)
					return new_frame;
			} catch {
				new_frame = null;
			}

			return null;
		}

		internal override StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory,
							  byte[] code, int offset)
		{
			StackFrame new_frame;
			if ((code != null) && (code.Length > 3)) {
				new_frame = read_prologue (frame, memory, code, offset);
				if (new_frame != null)
					return new_frame;
			}

			TargetAddress ebp = frame.FrameAddress;

			new_frame = do_hacks (frame, memory);
			if (new_frame != null)
				return new_frame;

			int addr_size = TargetAddressSize;

			Registers regs = new Registers (this);

			TargetAddress new_ebp = memory.ReadAddress (ebp);
			regs [(int) I386Register.EBP].SetValue (ebp, new_ebp);

			TargetAddress new_eip = memory.ReadAddress (ebp + addr_size);
			regs [(int) I386Register.EIP].SetValue (ebp + addr_size, new_eip);

			TargetAddress new_esp = ebp + 2 * addr_size;
			regs [(int) I386Register.ESP].SetValue (ebp, new_esp);

			ebp -= addr_size;

			return CreateFrame (frame.Thread, new_eip, new_esp, new_ebp, regs, true);
		}

		internal override StackFrame TrySpecialUnwind (StackFrame last_frame,
							       TargetMemoryAccess memory)
		{
			return null;
		}

		internal override void Hack_ReturnNull (Inferior inferior)
		{
			Registers regs = inferior.GetRegisters ();
			TargetAddress esp = new TargetAddress (
				AddressDomain, regs [(int) I386Register.ESP].GetValue ());
			TargetAddress eip = inferior.ReadAddress (esp);
			esp += TargetAddressSize;

			regs [(int) I386Register.EIP].SetValue (eip);
			regs [(int) I386Register.ESP].SetValue (esp);
			regs [(int) I386Register.EAX].SetValue (TargetAddress.Null);

			inferior.SetRegisters (regs);
		}

		protected override TargetAddress AdjustReturnAddress (Thread thread,
								      TargetAddress address)
		{
			TargetBinaryReader reader = thread.ReadMemory (address-7, 7).GetReader ();

			byte[] code = reader.ReadBuffer (7);
			if (code [2] == 0xe8)
				return address-5;

			if ((code [1] == 0xff) &&
			    ((code [2] & 0x38) == 0x10) && ((code [2] >> 6) == 2))
				return address-6;
			else if ((code [4] == 0xff) &&
				 ((code [5] & 0x38) == 0x10) && ((code [5] >> 6) == 1))
				return address-3;
			else if ((code [5] == 0xff) &&
				 ((code [6] & 0x38) == 0x10) && ((code [6] >> 6) == 3))
				return address-2;
			else if ((code [5] == 0xff) &&
				 ((code [6] & 0x38) == 0x10) && ((code [6] >> 6) == 0))
				return address-2;

			return address;
		}

		internal override StackFrame CreateFrame (Thread thread, Registers regs,
							  bool adjust_retaddr)
		{
			TargetAddress address = new TargetAddress (
				AddressDomain, regs [(int) I386Register.EIP].GetValue ());
			TargetAddress stack_pointer = new TargetAddress (
				AddressDomain, regs [(int) I386Register.ESP].GetValue ());
			TargetAddress frame_pointer = new TargetAddress (
				AddressDomain, regs [(int) I386Register.EBP].GetValue ());

			Console.WriteLine ("CREATE FRAME: {0} {1} {2}", address, stack_pointer,
					   frame_pointer);

			return CreateFrame (thread, address, stack_pointer, frame_pointer, regs,
					    adjust_retaddr);
		}

		internal override StackFrame GetLMF (Thread thread)
		{
			TargetAddress lmf = thread.ReadAddress (thread.LMFAddress);

			Console.WriteLine ("GET LMF: {0}", lmf);

			TargetBinaryReader reader = thread.ReadMemory (lmf, 32).GetReader ();

			reader.Position = 12;

			TargetAddress ebx = reader.ReadTargetAddress ();
			TargetAddress edi = reader.ReadTargetAddress ();
			TargetAddress esi = reader.ReadTargetAddress ();
			TargetAddress ebp = reader.ReadTargetAddress ();

			Console.WriteLine ("GET LMF #1: {0} - {1} {2} {3} {4}", lmf,
					   ebx, edi, esi, ebp);

			Registers regs = new Registers (this);
			regs [(int) I386Register.EBX].SetValue (lmf + 12, ebx);
			regs [(int) I386Register.EDI].SetValue (lmf + 16, edi);
			regs [(int) I386Register.ESI].SetValue (lmf + 20, esi);
			regs [(int) I386Register.EBP].SetValue (lmf + 24, ebp);

			TargetAddress new_eip = thread.ReadAddress (ebp + 4);
			regs [(int) I386Register.EIP].SetValue (ebp + 4, new_eip);

			TargetAddress new_ebp = thread.ReadAddress (ebp);
			regs [(int) I386Register.EBP].SetValue (ebp, new_ebp);

			TargetAddress new_esp = ebp + 8;
			regs [(int) I386Register.ESP].SetValue (ebp, new_esp);

			ebp -= 4;

			return CreateFrame (thread, new_eip, new_esp, new_ebp, regs, true);
		}
	}
}
