using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	public interface IArchitecture
	{
		// <summary>
		//   The names of all registers.
		// </summary>
		string[] RegisterNames {
			get;
		}

		// <summary>
		//   Indices of the "important" registers, sorted in a way that's suitable
		//   to display them to the user.
		// </summary>
		int[] RegisterIndices {
			get;
		}

		int[] AllRegisterIndices {
			get;
		}

		int[] RegisterSizes {
			get;
		}

		int CountRegisters {
			get;
		}

		string PrintRegister (Register register);

		string PrintRegisters (StackFrame frame);

		// <summary>
		//   Returns whether the instruction at target address @address is a `ret'
		//   instruction.
		// </summary>
		bool IsRetInstruction (ITargetMemoryAccess memory, TargetAddress address);

		// <summary>
		//   Check whether the instruction at target address @address is a `call'
		//   instruction and returns the destination of the call or null.
		//
		//   The out parameter @insn_size is set to the size on bytes of the call
		//   instructions.  This can be used to set a breakpoint immediately after
		//   the function.
		// </summary>
		TargetAddress GetCallTarget (ITargetMemoryAccess target, TargetAddress address,
					     out int insn_size);

		// <summary>
		//   Check whether the instruction at target address @address is a trampoline method.
		//   If it's a trampoline, return the address of the corresponding method's
		//   code.  For JIT trampolines, this should do a JIT compilation of the method.
		// </summary>
		TargetAddress GetTrampoline (ITargetMemoryAccess target, TargetAddress address,
					     TargetAddress generic_trampoline_address);

		int MaxPrologueSize {
			get;
		}

		SimpleStackFrame UnwindStack (StackFrame frame, byte[] code,
					      ITargetMemoryAccess memory);
	}
}
