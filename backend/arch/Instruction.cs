using System;

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

		public abstract bool HasInstructionSize {
			get;
		}

		public abstract int InstructionSize {
			get;
		}

		public abstract TargetAddress GetEffectiveAddress (TargetMemoryAccess memory);
	}
}
