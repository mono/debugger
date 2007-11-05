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
			Console.WriteLine ("INTERPRET INSTRUCTION: {0}", InstructionType);

			switch (InstructionType) {
			case Type.IndirectJump:
			case Type.Jump: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Console.WriteLine ("INTERPRET JUMP: {0}", target);
				Registers regs = inferior.GetRegisters ();
				regs [(int) I386Register.EIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.IndirectCall:
			case Type.Call: {
				TargetAddress target = GetEffectiveAddress (inferior);
				Console.WriteLine ("INTERPRET CALL: {0}", target);
				Registers regs = inferior.GetRegisters ();

				TargetAddress eip = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.EIP].Value);
				TargetAddress esp = new TargetAddress (
					inferior.AddressDomain, regs [(int) I386Register.ESP].Value);

				Console.WriteLine ("INTERPRET CALL #1: {0} {1} {2} {3}",
						   eip, esp, CallTarget, InstructionSize);

				inferior.WriteAddress (esp - 4, eip + InstructionSize);

				regs [(int) I386Register.ESP].SetValue (esp - 4);
				regs [(int) I386Register.EIP].SetValue (target);
				inferior.SetRegisters (regs);
				return true;
			}

			case Type.Ret: {
				Console.WriteLine ("INTERPRET RET: {0}", Displacement);

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

			TargetBinaryReader reader = memory.ReadMemory (call_target, 10).GetReader ();
			Console.WriteLine ("GET MONO TRAMPOLINE: {0} {1}", call_target,
					   reader.HexDump ());

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
			Console.WriteLine ("GET MONO TRAMPOLINE #1: {0}", call);
			if (!Opcodes.Process.MonoLanguage.IsTrampolineAddress (call)) {
				trampoline = TargetAddress.Null;
				return false;
			}

			Console.WriteLine ("GET MONO TRAMPOLINE #2: {0:x}", call_target);
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
