using System;
using System.Collections;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	internal class Architecture_I386 : X86_Architecture
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

		public override int[] AllRegisterIndices {
			get {
				return new int[] {
					(int) X86_Register.RAX, (int) X86_Register.RCX,
					(int) X86_Register.RDX, (int) X86_Register.RBX,
					(int) X86_Register.RSP, (int) X86_Register.RBP,
					(int) X86_Register.RSI, (int) X86_Register.RDI,

					(int) X86_Register.RIP,
					(int) X86_Register.EFLAGS,
					(int) X86_Register.ORIG_RAX,

					(int) X86_Register.CS,  (int) X86_Register.SS,
					(int) X86_Register.DS,  (int) X86_Register.ES,
					(int) X86_Register.FS,  (int) X86_Register.GS,

					(int) X86_Register.FS_BASE,
					(int) X86_Register.GS_BASE
				};
			}
		}

		public override int[] RegisterIndices {
			get {
				return new int[] {
					(int) X86_Register.RAX, (int) X86_Register.RCX,
					(int) X86_Register.RDX, (int) X86_Register.RBX,
					(int) X86_Register.RSP, (int) X86_Register.RBP,
					(int) X86_Register.RSI, (int) X86_Register.RDI,

					(int) X86_Register.RIP, (int) X86_Register.EFLAGS
				};
			}
		}

		// FIXME: Map mono/arch/x86/x86-codegen.h registers to
		//        debugger/arch/IArchitecture_I386.cs registers.
		internal override int[] RegisterMap {
			get {
				return new int[] {
					(int) X86_Register.RAX, (int) X86_Register.RCX,
					(int) X86_Register.RDX, (int) X86_Register.RBX,
					(int) X86_Register.RSP, (int) X86_Register.RBP,
					(int) X86_Register.RSI, (int) X86_Register.RDI
				};
			}
		}

		internal override int[] DwarfFrameRegisterMap {
			get {
				return new int[] {
					(int) X86_Register.RIP, (int) X86_Register.RSP,
					(int) X86_Register.RBP,

					(int) X86_Register.RAX, (int) X86_Register.RBX,
					(int) X86_Register.RCX, (int) X86_Register.RDX,
					(int) X86_Register.RSP, (int) X86_Register.RBP,
					(int) X86_Register.RSI, (int) X86_Register.RDI,
				};
			}
		}


		public override string[] RegisterNames {
			get {
				return new string[] {
					"eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi",
					null, null, null, null, null, null, null, null,
					"eip", "eflags", null, "cs", "ss", "ds", "es",
					"fs", "gs", null, null
				};
			}
		}

		public override int[] RegisterSizes {
			get {
				return new int[] {
					4, 4, 4, 4, 4, 4, 4, 4, -1, -1, -1, -1, -1, -1, -1, -1,
					4, 4, -1, 4, 4, 4, 4, 4, 4, -1, -1
				};
			}
		}

		public override string PrintRegister (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			switch ((X86_Register) register.Index) {
			case X86_Register.EFLAGS: {
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
				format (registers [(int) X86_Register.RAX]),
				format (registers [(int) X86_Register.RBX]),
				format (registers [(int) X86_Register.RCX]),
				format (registers [(int) X86_Register.RDX]),
				format (registers [(int) X86_Register.RSI]),
				format (registers [(int) X86_Register.RDI]),
				format (registers [(int) X86_Register.RBP]),
				format (registers [(int) X86_Register.RSP]),
				format (registers [(int) X86_Register.RIP]),
				PrintRegister (registers [(int) X86_Register.EFLAGS]));
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
				memory.AddressDomain, old_regs [(int) X86_Register.RBP].GetValue ());

			int addr_size = TargetAddressSize;
			TargetAddress new_ebp = memory.ReadAddress (ebp);
			regs [(int) X86_Register.RBP].SetValue (ebp, new_ebp);

			TargetAddress new_eip = memory.ReadAddress (ebp + addr_size);
			regs [(int) X86_Register.RIP].SetValue (ebp + addr_size, new_eip);

			TargetAddress new_esp = ebp + 2 * addr_size;
			regs [(int) X86_Register.RSP].SetValue (ebp, new_esp);

			regs [(int) X86_Register.RSI].Valid = true;
			regs [(int) X86_Register.RDI].Valid = true;

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
					regs [(int) X86_Register.RAX].SetValue (ebp, value);
					break;
				case 0x51: /* ecx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) X86_Register.RCX].SetValue (ebp, value);
					break;
				case 0x52: /* edx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) X86_Register.RDX].SetValue (ebp, value);
					break;
				case 0x53: /* ebx */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) X86_Register.RBX].SetValue (ebp, value);
					break;
				case 0x56: /* esi */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) X86_Register.RSI].SetValue (ebp, value);
					break;
				case 0x57: /* edi */
					value = (long) (uint) memory.ReadInteger (ebp);
					regs [(int) X86_Register.RDI].SetValue (ebp, value);
					break;
				}

				ebp -= addr_size;
			}

			return CreateFrame (frame.Thread, FrameType.Normal, memory, new_eip, new_esp, new_ebp, regs);
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
				regs [(int) X86_Register.RIP].SetValue (frame.StackPointer, new_eip);

				TargetAddress new_esp = frame.StackPointer + TargetAddressSize;
				TargetAddress new_ebp = frame.FrameAddress;

				regs [(int) X86_Register.RSP].SetValue (new_esp);

				return CreateFrame (frame.Thread, FrameType.Normal, memory, new_eip, new_esp, new_ebp, regs);
			}

			// push %ebp
			if (code [pos++] != 0x55)
				return null;

			if (pos >= offset) {
				Registers old_regs = frame.Registers;
				Registers regs = new Registers (old_regs);

				int addr_size = TargetAddressSize;
				TargetAddress new_ebp = memory.ReadAddress (frame.StackPointer);
				regs [(int) X86_Register.RBP].SetValue (frame.StackPointer, new_ebp);

				TargetAddress new_esp = frame.StackPointer + addr_size;
				TargetAddress new_eip = memory.ReadAddress (new_esp);
				regs [(int) X86_Register.RIP].SetValue (new_esp, new_eip);
				new_esp -= addr_size;

				regs [(int) X86_Register.RSP].SetValue (new_esp);

				return CreateFrame (frame.Thread, FrameType.Normal, memory, new_eip, new_esp, new_ebp, regs);
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

			regs [(int) X86_Register.RBP].Valid = true;
			regs [(int) X86_Register.RBX].Valid = true;
			regs [(int) X86_Register.RSI].Valid = true;
			regs [(int) X86_Register.RDI].Valid = true;

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

			regs [(int)X86_Register.RBX].SetValue (esp + 0x2c, ebx);
			regs [(int)X86_Register.RSI].SetValue (esp + 0x30, esi);
			regs [(int)X86_Register.RDI].SetValue (esp + 0x34, edi);
			regs [(int)X86_Register.RBP].SetValue (esp + 0x38, ebp);
			regs [(int)X86_Register.RIP].SetValue (esp + 0x3c, eip);

			esp += 0x40;
			regs [(int)X86_Register.RSP].SetValue (esp.Address);

			return CreateFrame (frame.Thread, FrameType.Normal, memory, eip, esp, ebp, regs);
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

			regs [(int)X86_Register.RIP].SetValue (esp, new_eip);
			regs [(int)X86_Register.RBP].SetValue (new_ebp);

			return CreateFrame (frame.Thread, FrameType.Normal, memory, new_eip, new_esp, new_ebp, regs);
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
			regs [(int) X86_Register.RBP].SetValue (ebp, new_ebp);

			TargetAddress new_eip = memory.ReadAddress (ebp + addr_size);
			regs [(int) X86_Register.RIP].SetValue (ebp + addr_size, new_eip);

			TargetAddress new_esp = ebp + 2 * addr_size;
			regs [(int) X86_Register.RSP].SetValue (ebp, new_esp);

			ebp -= addr_size;

			return CreateFrame (frame.Thread, FrameType.Normal, memory, new_eip, new_esp, new_ebp, regs);
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
				inferior.AddressDomain, regs [(int) X86_Register.RSP].GetValue ());
			TargetAddress eip = inferior.ReadAddress (esp);
			esp += TargetAddressSize;

			regs [(int) X86_Register.RIP].SetValue (eip);
			regs [(int) X86_Register.RSP].SetValue (esp);
			regs [(int) X86_Register.RAX].SetValue (TargetAddress.Null);

			inferior.SetRegisters (regs);
		}

		internal override StackFrame CreateFrame (Thread thread, FrameType type, TargetMemoryAccess memory, Registers regs)
		{
			TargetAddress address = new TargetAddress (
				memory.AddressDomain, regs [(int) X86_Register.RIP].GetValue ());
			TargetAddress stack_pointer = new TargetAddress (
				memory.AddressDomain, regs [(int) X86_Register.RSP].GetValue ());
			TargetAddress frame_pointer = new TargetAddress (
				memory.AddressDomain, regs [(int) X86_Register.RBP].GetValue ());

			return CreateFrame (thread, type, memory, address, stack_pointer, frame_pointer, regs);
		}

		internal override StackFrame GetLMF (ThreadServant thread, TargetMemoryAccess memory,
						     ref TargetAddress lmf_address)
		{
			TargetAddress lmf = lmf_address;

			TargetBinaryReader reader = memory.ReadMemory (lmf, 36).GetReader ();
			lmf_address = reader.ReadTargetAddress (); // prev

			reader.Position = 16;

			TargetAddress ebx = reader.ReadTargetAddress ();
			TargetAddress edi = reader.ReadTargetAddress ();
			TargetAddress esi = reader.ReadTargetAddress ();
			TargetAddress ebp = reader.ReadTargetAddress ();
			TargetAddress eip = reader.ReadTargetAddress ();

			Registers regs = new Registers (this);
			regs [(int) X86_Register.RBX].SetValue (lmf + 16, ebx);
			regs [(int) X86_Register.RDI].SetValue (lmf + 20, edi);
			regs [(int) X86_Register.RSI].SetValue (lmf + 24, esi);
			regs [(int) X86_Register.RBP].SetValue (lmf + 28, ebp);
			regs [(int) X86_Register.RIP].SetValue (lmf + 32, eip);

			TargetAddress new_ebp = memory.ReadAddress (ebp);
			regs [(int) X86_Register.RBP].SetValue (ebp, new_ebp);

			TargetAddress new_esp = ebp + 8;
			regs [(int) X86_Register.RSP].SetValue (ebp, new_esp);

			return CreateFrame (thread.Client, FrameType.LMF, memory, eip, new_esp, new_ebp, regs);
		}
	}
}
