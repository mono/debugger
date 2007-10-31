using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal class Instruction_I386 : X86_Instruction
	{
		internal Instruction_I386 (X86_Opcodes opcodes, TargetAddress address)
			: base (opcodes, address)
		{ }

		public override bool Is64BitMode {
			get { return false; }
		}

		protected override int DecodeRegister (int register)
		{
			switch (register) {
			case 0: /* eax */
				return (int) I386Register.EAX;
			case 1: /* ecx */
				return (int) I386Register.ECX;
			case 2: /* edx */
				return (int) I386Register.EDX;
			case 3: /* ebx */
				return (int) I386Register.EBX;
			case 4: /* esp */
				return (int) I386Register.ESP;
			case 5: /* ebp */
				return (int) I386Register.EBP;
			case 6: /* esi */
				return (int) I386Register.ESI;
			case 7: /* edi */
				return (int) I386Register.EDI;
			default:
				/* can never happen */
				throw new InvalidOperationException ();
			}
		}

		public override bool InterpretInstruction (Inferior inferior)
		{
			return false;
		}

		public override TrampolineType CheckTrampoline (TargetMemoryAccess memory,
								out TargetAddress trampoline)
		{
			trampoline = TargetAddress.Null;
			return TrampolineType.None;
		}
	}
}
