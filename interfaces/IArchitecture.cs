using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	internal interface IArchitecture
	{
		// <summary>
		//   Check whether target address @address is a `call' instruction and
		//   returns the destination of the call or null.  The out parameter
		//   @insn_size is set to the size on bytes of the call instructions.  This
		//   can be used to set a breakpoint immediately after the function.
		// </summary>
		TargetAddress GetCallTarget (TargetAddress address, out int insn_size);

		// <summary>
		//   Check whether target address @address is a trampoline method.
		//   If it's a trampoline, return the address of the corresponding method's
		//   code.  For JIT trampolines, this should do a JIT compilation of the method.
		// </summary>
		TargetAddress GetTrampoline (TargetAddress address,
					     TargetAddress generic_trampoline_address);
	}
}
