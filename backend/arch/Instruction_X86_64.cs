using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal class Instruction_X86_64 : X86_Instruction
	{
		internal Instruction_X86_64 (Opcodes_X86 opcodes, TargetAddress address)
			: base (opcodes, address)
		{ }

		public override bool Is64BitMode {
			get { return true; }
		}

		protected override int DecodeRegister (int register)
		{
			switch (register) {
			case 0: /* rax */
				return (int) X86_64_Register.RAX;
			case 1: /* rcx */
				return (int) X86_64_Register.RCX;
			case 2: /* rdx */
				return (int) X86_64_Register.RDX;
			case 3: /* rbx */
				return (int) X86_64_Register.RBX;
			case 4: /* rsp */
				return (int) X86_64_Register.RSP;
			case 5: /* rbp */
				return (int) X86_64_Register.RBP;
			case 6: /* rsi */
				return (int) X86_64_Register.RSI;
			case 7: /* rdi */
				return (int) X86_64_Register.RDI;
			case 8: /* r8 */
				return (int) X86_64_Register.R8;
			case 9: /* r9 */
				return (int) X86_64_Register.R9;
			case 10: /* r10 */
				return (int) X86_64_Register.R10;
			case 11: /* r11 */
				return (int) X86_64_Register.R11;
			case 12: /* r12 */
				return (int) X86_64_Register.R12;
			case 13: /* r13 */
				return (int) X86_64_Register.R13;
			case 14: /* r14 */
				return (int) X86_64_Register.R14;
			case 15: /* r15 */
				return (int) X86_64_Register.R15;
			default:
				/* can never happen */
				throw new InvalidOperationException ();
			}
		}

		public override bool InterpretInstruction (Inferior inferior)
		{
			Console.WriteLine ("INTERPRET INSTRUCTION: {0}", InstructionType);

			switch (InstructionType) {
			case Type.IndirectJump:
			case Type.Jump: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Console.WriteLine ("INTERPRET JUMP: {0}", target);
				Registers regs = inferior.GetRegisters ();
				regs [(int) X86_64_Register.RIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.IndirectCall:
			case Type.Call: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Console.WriteLine ("INTERPRET CALL: {0}", target);
				Registers regs = inferior.GetRegisters ();

				TargetAddress rip = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_64_Register.RIP].Value);
				TargetAddress rsp = new TargetAddress (
					inferior.AddressDomain, regs [(int) X86_64_Register.RSP].Value);

				Console.WriteLine ("INTERPRET CALL #1: {0} {1} {2} {3}",
						   rip, rsp, CallTarget, InstructionSize);

				inferior.WriteAddress (rsp - 8, rip + InstructionSize);

				regs [(int) X86_64_Register.RSP].SetValue (rsp - 8);
				regs [(int) X86_64_Register.RIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			default:
				return false;
			}
		}

		protected bool GetMonoTrampoline (TargetMemoryAccess memory,
						  out TargetAddress trampoline)
		{
			if ((InstructionType != Type.Call) && (InstructionType != Type.IndirectCall)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call_target = GetEffectiveAddress (memory);

			TargetBinaryReader reader = memory.ReadMemory (call_target, 14).GetReader ();
			byte opcode = reader.ReadByte ();
			if (opcode != 0xe8) {
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call = call_target + reader.ReadInt32 () + 5;
			Console.WriteLine ("GET MONO TRAMPOLINE: {0}", call);
			if (!Opcodes.Process.MonoLanguage.IsTrampolineAddress (call)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			long method;
			if (reader.ReadByte () == 0x04)
				method = reader.ReadInt32 ();
			else
				method = reader.ReadInt64 ();

			Console.WriteLine ("GET MONO TRAMPOLINE #1: {0:x}", method);
			trampoline = call_target;
			return true;
		}

		public override TrampolineType CheckTrampoline (TargetMemoryAccess memory,
								out TargetAddress trampoline)
		{
			Console.WriteLine ("CHECK TRAMPOLINE: {0} {1}", Address, InstructionType);

			if (InstructionType == Type.Call) {
				TargetAddress target = GetEffectiveAddress (memory);
				if (target.IsNull) {
					trampoline = TargetAddress.Null;
					return TrampolineType.None;
				}

				bool is_start;
				if (Opcodes.Process.BfdContainer.GetTrampoline (
					    memory, target, out trampoline, out is_start)) {
					target = trampoline;
					return is_start ? 
						TrampolineType.NativeTrampolineStart :
						TrampolineType.NativeTrampoline;
				}
			}

			if (Opcodes.Process.IsManagedApplication) {
				if (GetMonoTrampoline (memory, out trampoline))
					return TrampolineType.MonoTrampoline;
			}

			trampoline = TargetAddress.Null;
			return TrampolineType.None;
		}
	}
}
