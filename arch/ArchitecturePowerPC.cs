using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// Keep in sync with DebuggerPowerPCRegisters in backends/server/powerpc-arch.h.
	internal enum PowerPCRegister
	{
		R0	= 0,
		R1,
		R2,
		R3,
		R4,
		R5,
		R6,
		R7,
		R8,
		R9,
		R10,
		R11,
		R12,
		R13,
		R14,
		R15,
		R16,
		R17,
		R18,
		R19,
		R20,
		R21,
		R22,
		R23,
		R24,
		R25,
		R26,
		R27,
		R28,
		R29,
		R30,
		R31,

		PC,
		PS,
		CR,
		LR,
		CTR,
		XER,
		MQ,
		VRSAVE
	}

	// <summary>
	//   Architecture-dependent stuff for the powerpc
	// </summary>
	internal class ArchitecturePowerPC : IArchitecture
	{
		public bool IsRetInstruction (ITargetMemoryAccess memory, TargetAddress address)
		{
			return false;
		}

		public TargetAddress GetCallTarget (ITargetMemoryAccess target,
						    TargetAddress address, out int insn_size)
		{
			insn_size = 0;
			return TargetAddress.Null;
		}

		public TargetAddress GetTrampoline (ITargetMemoryAccess target,
						    TargetAddress location,
						    TargetAddress trampoline_address)
		{
			throw new NotImplementedException ();
		}

		int[] all_regs = { (int) PowerPCRegister.R0,
				   (int) PowerPCRegister.R1,
				   (int) PowerPCRegister.R2,
				   (int) PowerPCRegister.R3,
				   (int) PowerPCRegister.R4,
				   (int) PowerPCRegister.R5,
				   (int) PowerPCRegister.R6,
				   (int) PowerPCRegister.R7,
				   (int) PowerPCRegister.R8,
				   (int) PowerPCRegister.R9,
				   (int) PowerPCRegister.R10,
				   (int) PowerPCRegister.R11,
				   (int) PowerPCRegister.R12,
				   (int) PowerPCRegister.R13,
				   (int) PowerPCRegister.R14,
				   (int) PowerPCRegister.R15,
				   (int) PowerPCRegister.R16,
				   (int) PowerPCRegister.R17,
				   (int) PowerPCRegister.R18,
				   (int) PowerPCRegister.R19,
				   (int) PowerPCRegister.R20,
				   (int) PowerPCRegister.R21,
				   (int) PowerPCRegister.R22,
				   (int) PowerPCRegister.R23,
				   (int) PowerPCRegister.R24,
				   (int) PowerPCRegister.R25,
				   (int) PowerPCRegister.R26,
				   (int) PowerPCRegister.R27,
				   (int) PowerPCRegister.R28,
				   (int) PowerPCRegister.R29,
				   (int) PowerPCRegister.R30,
				   (int) PowerPCRegister.R31,

				   (int) PowerPCRegister.PC,
				   (int) PowerPCRegister.PS,
				   (int) PowerPCRegister.CR,
				   (int) PowerPCRegister.LR,
				   (int) PowerPCRegister.CTR,
				   (int) PowerPCRegister.XER,
				   (int) PowerPCRegister.MQ,
				   (int) PowerPCRegister.VRSAVE
		};

		string[] registers = { "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7",
				       "r8", "r9", "r10", "r11", "r12", "r13", "r14",
				       "r15", "r16", "r17", "r18", "r19", "r20", "r21",
				       "r22", "r23", "r24", "r25", "r26", "r27", "r28",
				       "r29", "r30", "r31", "pc", "ps", "cr", "lr", "ctr",
				       "xer", "mq", "vrsave" };

		public string[] RegisterNames {
			get {
				return registers;
			}
		}

		public int[] RegisterIndices {
			get {
				return all_regs;
			}
		}

		public int[] AllRegisterIndices {
			get {
				return all_regs;
			}
		}

		public string PrintRegister (Register register)
		{
			return String.Format ("{0:x}", (long) register.Data);
		}

		public string PrintRegisters (StackFrame frame)
		{
			return null;
		}

		public int MaxPrologueSize {
			get { return 50; }
		}

		public object UnwindStack (Register[] registers)
		{
			throw new NotImplementedException ();
		}

		public Register[] UnwindStack (byte[] code, ITargetMemoryAccess memory,
					       object last_data, out object new_data)
		{
			throw new NotImplementedException ();
		}
	}
}
