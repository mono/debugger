using System;

namespace Mono.Debugger
{
	// <summary>
	//   Architecture-dependent stuff for the i386.
	// </summary>
	public class ArchitectureI386 : IArchitecture
	{
		public long GetCallTarget (IDebuggerBackend backend, long address)
		{
			byte opcode = backend.ReadByte (address);

			if (opcode == 0xe8) {
				int target = backend.ReadSignedInteger (address + 1);
				Console.WriteLine ("CALL: {0:x}", address + target);
				return address + target;
			}

			return 0;
		}
	}
}
