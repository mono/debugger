using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal abstract class Instruction : DebuggerMarshalByRefObject
	{
		public enum Type
		{
			Unknown,
			ConditionalJump,
			IndirectCall,
			Call,
			IndirectJump,
			Jump
		}

		public abstract Type InstructionType {
			get;
		}

		public abstract bool IsIpRelative {
			get;
		}

		public abstract bool HasInstructionSize {
			get;
		}

		public abstract int InstructionSize {
			get;
		}

		public abstract byte[] Code {
			get;
		}

		public abstract TargetAddress GetEffectiveAddress (TargetMemoryAccess memory);

		public abstract bool InterpretInstruction (Inferior inferior);
	}
}
