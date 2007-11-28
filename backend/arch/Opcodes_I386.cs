using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_I386 : X86_Opcodes
	{
		internal Opcodes_I386 (ProcessServant process)
			: base (process)
		{ }

		public override bool Is64BitMode {
			get { return false; }
		}

		internal override byte[] GenerateNopInstruction ()
		{
			return new byte[] { 0x90 };
		}
	}
}
