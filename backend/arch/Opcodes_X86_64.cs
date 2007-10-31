using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_X86_64 : X86_Opcodes
	{
		internal Opcodes_X86_64 (ProcessServant process)
			: base (process)
		{ }

		public override bool Is64BitMode {
			get { return true; }
		}

		internal override byte[] GenerateNopInstruction ()
		{
			return new byte[] { 0x90 };
		}
	}
}
