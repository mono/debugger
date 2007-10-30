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

		public enum TrampolineType
		{
			None,
			NativeTrampolineStart,
			NativeTrampoline,
			MonoTrampoline
		}

		public abstract TargetAddress Address {
			get;
		}

		public abstract Type InstructionType {
			get;
		}

		public abstract bool IsIpRelative {
			get;
		}

		public bool IsCall {
			get {
				return (InstructionType == Type.Call) ||
					(InstructionType == Type.IndirectCall);
			}
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

		public abstract TrampolineType CheckTrampoline (TargetMemoryAccess memory,
								out TargetAddress trampoline);

		public abstract bool InterpretInstruction (Inferior inferior);
	}
}
