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
			return false;
		}

		public TargetAddress GetCallTarget (ITargetMemoryAccess target, TargetAddress address, out int insn_size)
		{
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
				"RAX={0}  RBX={1}  RCX={2}  RDX={3}  RSI={4}  RDI={5}\n" +
				"RBP={6}  RSP={7}  RIP={8}  EFLAGS={9}\n",
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
