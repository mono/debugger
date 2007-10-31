using System;
using Mono.Debugger.Backends;

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

		internal override byte[] GenerateJumpInstruction (TargetAddress address)
		{
			return new byte[0];
		}
	}
}
