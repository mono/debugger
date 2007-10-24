using System;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_X86 : Opcodes
	{
		public readonly bool Is64BitMode;

		internal Opcodes_X86 (bool is_64bit)
		{
			this.Is64BitMode = is_64bit;
		}

		internal override void ReadInstruction (TargetMemoryAccess memory,
							TargetAddress address)
		{
			try {
				X86_Instruction.DecodeInstruction (memory, address);
			} catch {
			}
		}


	}
}
