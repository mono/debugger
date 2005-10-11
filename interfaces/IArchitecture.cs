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

		// <summary>
		//   Indices of all registers.
		// </summary>
		int[] AllRegisterIndices {
			get;
		}

		// <summary>
		// Size (in bytes) of each register.
		// </summary>
		int[] RegisterSizes {
			get;
		}

		// <summary>
		// A map between the register the register numbers in
		// the jit code generator and the register indices
		// used in the above arrays.
		// </summary>
		int[] RegisterMap {
			get;
		}

		int[] DwarfFrameRegisterMap {
			get;
		}

		// <summary>
		// The length of the
		// AllRegisterIndices/RegisterNames/RegisterSizes
		// arrays.
		// XXX why not just let people do
		// "RegisterNames.Length"?
		// </summary>
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
		//   instruction and returns the destination of the call or TargetAddress.Null.
		//
		//   The out parameter @insn_size is set to the size on bytes of the call
		//   instructions.  This can be used to set a breakpoint immediately after
		//   the function.
		// </summary>
		TargetAddress GetCallTarget (ITargetMemoryAccess target, TargetAddress address,
					     out int insn_size);

		// <summary>
		//   Check whether the instruction at target address @address is a `jump'
		//   instruction and returns the destination of the call or TargetAddress.Null.
		//
		//   The out parameter @insn_size is set to the size on bytes of the jump
		//   instructions.  This can be used to set a breakpoint immediately after
		//   the jump.
		// </summary>
		TargetAddress GetJumpTarget (ITargetMemoryAccess target, TargetAddress address,
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

		SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
					      SimpleStackFrame frame, Symbol name,
					      byte[] code);

		SimpleStackFrame UnwindStack (ITargetMemoryAccess memory, TargetAddress stack,
					      TargetAddress frame_address);

		SimpleStackFrame TrySpecialUnwind (ITargetMemoryAccess memory, SimpleStackFrame frame);
	}
}
