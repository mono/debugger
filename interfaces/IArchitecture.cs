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
		//   returns the destination of the call or null.
		// </summary>
		long GetCallTarget (IDebuggerBackend backend, long address);
	}
}
