using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal abstract class Opcodes : DebuggerMarshalByRefObject
	{
		internal abstract void ReadInstruction (TargetMemoryAccess memory,
							TargetAddress address);
	}
}
