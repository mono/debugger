using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	internal interface IArchitecture
	{
		// <summary>
		//   The address of the JIT's generic trampoline code.
		// </summary>
		ITargetLocation GenericTrampolineCode {
			get; set;
		}

		// <summary>
		//   Check whether target address @address is a `call' instruction and
		//   returns the destination of the call or null.  The out parameter
		//   @insn_size is set to the size on bytes of the call instructions.  This
		//   can be used to set a breakpoint immediately after the function.
		// </summary>
		ITargetLocation GetCallTarget (ITargetLocation address, out int insn_size);

		// <summary>
		//   Check whether target address @address is a trampoline method.
		//   If it's a trampoline, return the address of the corresponding method's
		//   code.  For JIT trampolines, this should do a JIT compilation of the method.
		// </summary>
		ITargetLocation GetTrampoline (ITargetLocation address);
	}
}
