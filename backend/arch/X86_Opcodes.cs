using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal abstract class X86_Opcodes : Opcodes
	{
		ProcessServant process;
		TargetMemoryInfo target_info;
		BfdDisassembler disassembler;

		protected X86_Opcodes (ProcessServant process)
		{
			this.process = process;

			target_info = Inferior.GetTargetMemoryInfo (AddressDomain.Global);
			disassembler = new BfdDisassembler (null, Is64BitMode);
		}

		public abstract bool Is64BitMode {
			get;
		}

		internal TargetMemoryInfo TargetMemoryInfo {
			get { return target_info; }
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
