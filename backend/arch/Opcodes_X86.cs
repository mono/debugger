using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_X86 : Opcodes
	{
		public readonly bool Is64BitMode;

		BfdDisassembler disassembler;

		internal Opcodes_X86 (bool is_64bit)
		{
			this.Is64BitMode = is_64bit;

			disassembler = new BfdDisassembler (null, is_64bit);
		}

#if FIXME
		internal int GetInstructionSize (TargetAddress address)
		{
			int count = bfd_glue_disassemble_insn (dis, info, address.Address);
		}
#endif

		protected override void DoDispose ()
		{
			if (disassembler != null) {
				disassembler.Dispose ();
				disassembler = null;
			}
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
