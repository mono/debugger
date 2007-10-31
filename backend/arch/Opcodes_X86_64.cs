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

		internal override byte[] GenerateJumpInstruction (TargetAddress address)
		{
#if FIXME
			TargetBinaryWriter writer = new TargetBinaryWriter (
				14, inferior.TargetMemoryInfo);

			writer.WriteByte (0xff);
			writer.WriteByte (0x25);
			writer.WriteInt32 (0);
			writer.WriteAddress (target);

			return writer.Contents;
#else
			return new byte [0];
#endif
		}
	}
}
