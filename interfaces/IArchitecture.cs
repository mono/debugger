using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	public interface IArchitecture
	{
		// <summary>
		//   Check whether target address @address is a `call' instruction and
		//   returns the destination of the call or null.  The out parameter
		//   @insn_size is set to the size on bytes of the call instructions.  This
		//   can be used to set a breakpoint immediately after the function.
		// </summary>
		long GetCallTarget (IDebuggerBackend backend, long address, out int insn_size);

		// <summary>
		//   Check whether target address @address is a trampoline method.
		//   If it's a trampoline, return the address of the corresponding method's
		//   code.  For JIT trampolines, this should do a JIT compilation of the method.
		// </summary>
		long GetTrampoline (IDebuggerBackend backend, long address, long generic_trampoline);
	}
}
