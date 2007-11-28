using System;
using Mono.Debugger.Backend;

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

		public override bool CanInterpretInstruction {
			get {
				switch (InstructionType) {
				case Type.IndirectJump:
				case Type.Jump:
				case Type.IndirectCall:
				case Type.Call:
				case Type.Ret:
				case Type.Interpretable:
					return true;

				default:
					return false;
				}
			}
		}

		public override bool InterpretInstruction (Inferior inferior)
		{
			switch (InstructionType) {
			case Type.IndirectJump:
			case Type.Jump: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Registers regs = inferior.GetRegisters ();
				regs [(int) I386Register.EIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.IndirectCall:
			case Type.Call: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Registers regs = inferior.GetRegisters ();

				TargetAddress eip = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.EIP].Value);
				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.ESP].Value);

				inferior.WriteAddress (esp - 4, eip + InstructionSize);

				regs [(int) I386Register.ESP].SetValue (esp - 4);
				regs [(int) I386Register.EIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Ret: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.ESP].Value);

				TargetAddress eip = inferior.ReadAddress (esp);
				esp += 4 + Displacement;

				regs [(int) I386Register.ESP].SetValue (esp);
				regs [(int) I386Register.EIP].SetValue (eip);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Interpretable: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.ESP].Value);
				TargetAddress ebp = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.EBP].Value);
				TargetAddress eip = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.EIP].Value);

				if (Code [0] == 0x55) /* push %ebp */ {
					inferior.WriteAddress (esp - 4, ebp);
					regs [(int) I386Register.ESP].SetValue (esp - 4);
					regs [(int) I386Register.EIP].SetValue (eip + 1);
					inferior.SetRegisters (regs);
					return true;
				}

				return false;
			}

			default:
				return false;
			}
		}

		protected override bool GetMonoTrampoline (TargetMemoryAccess memory,
							   TargetAddress call_target,
							   out TargetAddress trampoline)
		{
			TargetBinaryReader reader = memory.ReadMemory (call_target, 10).GetReader ();

			byte opcode = reader.ReadByte ();
			if (opcode != 0x68) {
				trampoline = TargetAddress.Null;
				return false;
			}

			reader.Position += 4;

			opcode = reader.ReadByte ();
			if (opcode != 0xe9) {
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call = call_target + reader.ReadInt32 () + 10;
			if (!Opcodes.Process.MonoLanguage.IsTrampolineAddress (call)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			trampoline = call_target;
			return true;
		}
	}
}
