using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal class X86_Opcodes : Opcodes
	{
		public readonly bool Is64BitMode;

		ProcessServant process;
		BfdDisassembler disassembler;

		internal X86_Opcodes (ProcessServant process, bool is_64bit)
		{
			this.process = process;
			this.Is64BitMode = is_64bit;

			disassembler = new BfdDisassembler (null, is_64bit);
		}

		internal Disassembler Disassembler {
			get { return disassembler; }
		}

		internal ProcessServant Process {
			get { return process; }
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
