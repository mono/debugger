using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Instruction_X86_64 : X86_Instruction
	{
		internal Instruction_X86_64 (X86_Opcodes opcodes, TargetAddress address)
			: base (opcodes, address)
		{ }

		public override bool Is64BitMode {
			get { return true; }
		}

		protected override int DecodeRegister (int register)
		{
			switch (register) {
			case 0: /* rax */
				return (int) X86_Register.RAX;
			case 1: /* rcx */
				return (int) X86_Register.RCX;
			case 2: /* rdx */
				return (int) X86_Register.RDX;
			case 3: /* rbx */
				return (int) X86_Register.RBX;
			case 4: /* rsp */
				return (int) X86_Register.RSP;
			case 5: /* rbp */
				return (int) X86_Register.RBP;
			case 6: /* rsi */
				return (int) X86_Register.RSI;
			case 7: /* rdi */
				return (int) X86_Register.RDI;
			case 8: /* r8 */
				return (int) X86_Register.R8;
			case 9: /* r9 */
				return (int) X86_Register.R9;
			case 10: /* r10 */
				return (int) X86_Register.R10;
			case 11: /* r11 */
				return (int) X86_Register.R11;
			case 12: /* r12 */
				return (int) X86_Register.R12;
			case 13: /* r13 */
				return (int) X86_Register.R13;
			case 14: /* r14 */
				return (int) X86_Register.R14;
			case 15: /* r15 */
				return (int) X86_Register.R15;
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

				TargetAddress rip = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RIP].Value);
				TargetAddress rsp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);

				inferior.WriteAddress (rsp - 8, rip + InstructionSize);

				regs [(int) X86_Register.RSP].SetValue (rsp - 8);
				regs [(int) X86_Register.RIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Ret: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress rsp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);

				TargetAddress rip = inferior.ReadAddress (rsp);
				rsp += 8 + Displacement;

				regs [(int) X86_Register.RSP].SetValue (rsp);
				regs [(int) X86_Register.RIP].SetValue (rip);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Interpretable: {
				Registers regs = inferior.GetRegisters ();

				TargetAddress rsp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RSP].Value);
				TargetAddress rbp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RBP].Value);
				TargetAddress rip = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_Register.RIP].Value);

				if (Code [0] == 0x55) /* push %rbp */ {
					inferior.WriteAddress (rsp - 8, rbp);
					regs [(int) X86_Register.RSP].SetValue (rsp - 8);
					regs [(int) X86_Register.RIP].SetValue (rip + 1);
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
			TargetBinaryReader reader = memory.ReadMemory (call_target, 14).GetReader ();
			byte opcode = reader.ReadByte ();
			if (opcode != 0xe8) {
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call = call_target + reader.ReadInt32 () + 5;
			if (!Opcodes.Process.MonoLanguage.IsTrampolineAddress (call)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			trampoline = call_target;
			return true;
		}
	}
}
