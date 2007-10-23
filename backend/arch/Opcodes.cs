using System;

namespace Mono.Debugger.Backends
{
	internal abstract class Opcodes : DebuggerMarshalByRefObject
	{
		internal abstract void ReadInstruction (TargetMemoryAccess memory,
							TargetAddress address);
	}
}
