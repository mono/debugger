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

		internal Disassembler Disassembler {
			get { return disassembler; }
		}

		protected override void DoDispose ()
		{
			if (disassembler != null) {
				disassembler.Dispose ();
				disassembler = null;
			}
		}

		internal override Instruction ReadInstruction (TargetMemoryAccess memory,
							       TargetAddress address)
		{
			return X86_Instruction.DecodeInstruction (this, memory, address);
		}
	}
}
