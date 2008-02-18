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
				return (int) X86_Register.RAX;
			case 1: /* ecx */
				return (int) X86_Register.RCX;
			case 2: /* edx */
				return (int) X86_Register.RDX;
			case 3: /* ebx */
				return (int) X86_Register.RBX;
			case 4: /* esp */
				return (int) X86_Register.RSP;
			case 5: /* ebp */
				return (int) X86_Register.RBP;
			case 6: /* esi */
				return (int) X86_Register.RSI;
			case 7: /* edi */
				return (int) X86_Register.RDI;
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
				regs [(int) X86_Register.RIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.IndirectCall:
			case Type.Call: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Registers regs = inferior.GetRegisters ();

				TargetAddress eip = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RIP].Value);
				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);

				inferior.WriteAddress (esp - 4, eip + InstructionSize);

				regs [(int) X86_Register.RSP].SetValue (esp - 4);
				regs [(int) X86_Register.RIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Ret: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);

				TargetAddress eip = inferior.ReadAddress (esp);
				esp += 4 + Displacement;

				regs [(int) X86_Register.RSP].SetValue (esp);
				regs [(int) X86_Register.RIP].SetValue (eip);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Interpretable: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);
				TargetAddress ebp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RBP].Value);
				TargetAddress eip = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RIP].Value);

				if (Code [0] == 0x55) /* push %ebp */ {
					inferior.WriteAddress (esp - 4, ebp);
					regs [(int) X86_Register.RSP].SetValue (esp - 4);
					regs [(int) X86_Register.RIP].SetValue (eip + 1);
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
			if (opcode == 0x6a)
				reader.Position ++;
			else if (opcode == 0x68)
				reader.Position += 4;
			else {
				trampoline = TargetAddress.Null;
				return false;
			}

			opcode = reader.ReadByte ();
			if (opcode != 0xe9) {
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call = call_target + reader.ReadInt32 () + reader.Position;
			if (!Opcodes.Process.MonoLanguage.IsTrampolineAddress (call)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			trampoline = call_target;
			return true;
		}
	}
}
