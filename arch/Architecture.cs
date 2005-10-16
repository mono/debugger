using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	public abstract class Architecture : MarshalByRefObject
	{
		protected readonly Debugger backend;

		protected Architecture (Debugger backend)
		{
			this.backend = backend;
		}

		// <summary>
		//   The names of all registers.
		// </summary>
		public abstract string[] RegisterNames {
			get;
		}

		// <summary>
		//   Indices of the "important" registers, sorted in a way that's suitable
		//   to display them to the user.
		// </summary>
		public abstract int[] RegisterIndices {
			get;
		}

		// <summary>
		//   Indices of all registers.
		// </summary>
		public abstract int[] AllRegisterIndices {
			get;
		}

		// <summary>
		// Size (in bytes) of each register.
		// </summary>
		public abstract int[] RegisterSizes {
			get;
		}

		// <summary>
		// A map between the register the register numbers in
		// the jit code generator and the register indices
		// used in the above arrays.
		// </summary>
		internal abstract int[] RegisterMap {
			get;
		}

		internal abstract int[] DwarfFrameRegisterMap {
			get;
		}

		internal abstract int CountRegisters {
			get;
		}

		public abstract string PrintRegister (Register register);

		public abstract string PrintRegisters (StackFrame frame);

		// <summary>
		//   Returns whether the instruction at target address @address is a `ret'
		//   instruction.
		// </summary>
		internal abstract bool IsRetInstruction (ITargetMemoryAccess memory,
							 TargetAddress address);

		// <summary>
		//   Check whether the instruction at target address @address is a `call'
		//   instruction and returns the destination of the call or TargetAddress.Null.
		//
		//   The out parameter @insn_size is set to the size on bytes of the call
		//   instructions.  This can be used to set a breakpoint immediately after
		//   the function.
		// </summary>
		internal abstract TargetAddress GetCallTarget (ITargetMemoryAccess target,
							       TargetAddress address,
							       out int insn_size);

		// <summary>
		//   Check whether the instruction at target address @address is a `jump'
		//   instruction and returns the destination of the call or TargetAddress.Null.
		//
		//   The out parameter @insn_size is set to the size on bytes of the jump
		//   instructions.  This can be used to set a breakpoint immediately after
		//   the jump.
		// </summary>
		internal abstract TargetAddress GetJumpTarget (ITargetMemoryAccess target,
							       TargetAddress address,
							       out int insn_size);

		// <summary>
		//   Check whether the instruction at target address @address is a trampoline method.
		//   If it's a trampoline, return the address of the corresponding method's
		//   code.  For JIT trampolines, this should do a JIT compilation of the method.
		// </summary>
		internal abstract TargetAddress GetTrampoline (ITargetMemoryAccess target,
							       TargetAddress address,
							       TargetAddress generic_trampoline_address);

		internal abstract int MaxPrologueSize {
			get;
		}

		internal abstract StackFrame UnwindStack (StackFrame last_frame,
							  ITargetMemoryAccess memory,
							  byte[] code, int offset);

		internal abstract StackFrame TrySpecialUnwind (StackFrame last_frame,
							       ITargetMemoryAccess memory);

		internal StackFrame CreateFrame (StackFrame last_frame, TargetAddress address,
						 TargetAddress stack, TargetAddress frame_pointer,
						 Registers regs)
		{
			if (address.IsNull)
				return null;

			Method method = backend.SymbolTableManager.Lookup (address);
			if (method != null)
				return new StackFrame (
					last_frame.Process, last_frame.TargetAccess, address,
					stack, frame_pointer, regs, method);

			Symbol name = backend.SymbolTableManager.SimpleLookup (address, false);
			return new StackFrame (
				last_frame.Process, last_frame.TargetAccess, address, stack,
				frame_pointer, regs, name);
		}
	}
}
