using System;

namespace Mono.Debugger.Backends
{
	internal class Opcodes_X86 : Opcodes
	{
		public const int MaxInstructionLength = 15;

		public readonly bool Is64BitMode;

		[Flags]
		protected enum X86_Prefix
		{
			REPZ	= 1,
			REPNZ	= 2,
			LOCK	= 4,
			CS	= 8,
			SS	= 16,
			DS	= 32,
			ES	= 64,
			FS	= 128,
			GS	= 256,
			DATA	= 512,
			ADDR	= 1024,
			FWAIT	= 2048
		}

		[Flags]
		protected enum X86_REX_Prefix
		{
			REX_B	= 1,
			REX_X	= 2,
			REX_R	= 4,
			REX_W	= 8
		}

		protected enum InstructionType
		{
			Unknown,
			ConditionalJump,
			IndirectCall,
			Call,
			IndirectJump,
			Jump
		}

		protected static string format_2_bits (int value)
		{
			char b1 = ((value & 0x02) != 0) ? '1' : '0';
			char b2 = ((value & 0x01) != 0) ? '1' : '0';
			return String.Concat (b1, b2);
		}

		protected static string format_4_bits (int value)
		{
			char b1 = ((value & 0x08) != 0) ? '1' : '0';
			char b2 = ((value & 0x04) != 0) ? '1' : '0';
			char b3 = ((value & 0x02) != 0) ? '1' : '0';
			char b4 = ((value & 0x01) != 0) ? '1' : '0';
			return String.Concat (b1, b2, b3, b4);
		}

		protected class ModRM
		{
			public readonly int Mod;
			public readonly int Reg;
			public readonly int R_M;

			public ModRM (X86_Instruction insn, byte modrm)
			{
				Mod = (modrm & 0xc0) >> 6;
				Reg = (modrm & 0x38) >> 3;
				R_M = (modrm & 0x7);

				if ((insn.RexPrefix & X86_REX_Prefix.REX_R) != 0)
					Reg |= 0x08;
				if ((insn.RexPrefix & X86_REX_Prefix.REX_B) != 0)
					R_M |= 0x08;

				Console.WriteLine ("MODRM: {0:x} - {1:x} {2:x} {3:x}",
						   modrm, Mod, Reg, R_M);
			}

			public override string ToString ()
			{
				return String.Format ("ModRM (mod={0}, reg={1}, r/m={2})",
						      format_2_bits (Mod), format_4_bits (Reg),
						      format_4_bits (R_M));
			}
		}

		protected class SIB
		{
			public readonly int Scale;
			public readonly int Index;
			public readonly int Base;

			public SIB (X86_Instruction insn, byte sib)
			{
				Scale = (sib & 0xc0) >> 6;
				Index = (sib & 0x38) >> 3;
				Base = (sib & 0x7);

				if ((insn.RexPrefix & X86_REX_Prefix.REX_X) != 0)
					Index |= 0x08;
				if ((insn.RexPrefix & X86_REX_Prefix.REX_B) != 0)
					Base |= 0x08;
			}

			public override string ToString ()
			{
				return String.Format ("SIB (scale={0}, index={1}, base={2})",
						      format_2_bits (Scale), format_4_bits (Index),
						      format_4_bits (Base));
			}
		}

		protected class X86_Instruction
		{
			public readonly TargetAddress Address;

			public X86_Instruction (TargetAddress address)
			{
				this.Address = address;
			}

			public X86_Prefix Prefix;
			public X86_REX_Prefix RexPrefix;

			public ModRM ModRM;
			public SIB SIB;

			/* For IndirectCall and IndirectJump */
			public int Register;
			public int IndexRegister;
			public int Displacement;
			public bool DereferenceAddress;

			public InstructionType Type = InstructionType.Unknown;

			public bool IsIpRelative;

			public bool HasInsnSize {
				get { return has_insn_size; }
			}

			public int InstructionSize {
				get {
					if (!has_insn_size)
						throw new InvalidOperationException ();

					return insn_size;
				}
			}

			internal void SetInstructionSize (int size)
			{
				this.insn_size = size;
				this.has_insn_size = true;
			}

			bool has_insn_size;
			int insn_size;

			public bool InsnSize;

			public TargetAddress CallTarget = TargetAddress.Null;
		}

		/*
		 * These two tables have been copied verbosely from i386-dis.c from libopcode.
		 */

		static readonly byte[] OneByte_Has_ModRM = {
			/*       0 1 2 3 4 5 6 7 8 9 a b c d e f        */
			/*       -------------------------------        */
			/* 00 */ 1,1,1,1,0,0,0,0,1,1,1,1,0,0,0,0, /* 00 */
			/* 10 */ 1,1,1,1,0,0,0,0,1,1,1,1,0,0,0,0, /* 10 */
			/* 20 */ 1,1,1,1,0,0,0,0,1,1,1,1,0,0,0,0, /* 20 */
			/* 30 */ 1,1,1,1,0,0,0,0,1,1,1,1,0,0,0,0, /* 30 */
			/* 40 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 40 */
			/* 50 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 50 */
			/* 60 */ 0,0,1,1,0,0,0,0,0,1,0,1,0,0,0,0, /* 60 */
			/* 70 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 70 */
			/* 80 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* 80 */
			/* 90 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 90 */
			/* a0 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* a0 */
			/* b0 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* b0 */
			/* c0 */ 1,1,0,0,1,1,1,1,0,0,0,0,0,0,0,0, /* c0 */
			/* d0 */ 1,1,1,1,0,0,0,0,1,1,1,1,1,1,1,1, /* d0 */
			/* e0 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* e0 */
			/* f0 */ 0,0,0,0,0,0,1,1,0,0,0,0,0,0,1,1  /* f0 */
			/*       -------------------------------        */
			/*       0 1 2 3 4 5 6 7 8 9 a b c d e f        */
		};

		static readonly byte[] TwoByte_Has_ModRM = {
			/*       0 1 2 3 4 5 6 7 8 9 a b c d e f        */
			/*       -------------------------------        */
			/* 00 */ 1,1,1,1,0,0,0,0,0,0,0,0,0,1,0,1, /* 0f */
			/* 10 */ 1,1,1,1,1,1,1,1,1,0,0,0,0,0,0,0, /* 1f */
			/* 20 */ 1,1,1,1,1,0,1,0,1,1,1,1,1,1,1,1, /* 2f */
			/* 30 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 3f */
			/* 40 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* 4f */
			/* 50 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* 5f */
			/* 60 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* 6f */
			/* 70 */ 1,1,1,1,1,1,1,0,0,0,0,0,0,0,1,1, /* 7f */
			/* 80 */ 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, /* 8f */
			/* 90 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* 9f */
			/* a0 */ 0,0,0,1,1,1,0,0,0,0,0,1,1,1,1,1, /* af */
			/* b0 */ 1,1,1,1,1,1,1,1,0,0,1,1,1,1,1,1, /* bf */
			/* c0 */ 1,1,1,1,1,1,1,1,0,0,0,0,0,0,0,0, /* cf */
			/* d0 */ 0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* df */
			/* e0 */ 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, /* ef */
			/* f0 */ 0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,0  /* ff */
			/*       -------------------------------        */
			/*       0 1 2 3 4 5 6 7 8 9 a b c d e f        */
		};

		internal Opcodes_X86 (bool is_64bit)
		{
			this.Is64BitMode = is_64bit;
		}

		internal override void ReadInstruction (TargetMemoryAccess memory,
							TargetAddress address)
		{
			try {
				TargetReader reader = new TargetReader (
					memory.ReadMemory (address, MaxInstructionLength));
				DecodeInstruction (reader, address);
			} catch {
			}
		}

		bool CheckPrefix (X86_Instruction insn, TargetReader reader)
		{
			byte opcode = reader.PeekByte ();
			if ((opcode >= 0x40) && (opcode <= 0x4f)) {
				if (!Is64BitMode)
					return false;

				if ((opcode & 0x01) != 0)
					insn.RexPrefix |= X86_REX_Prefix.REX_B;
				if ((opcode & 0x02) != 0)
					insn.RexPrefix |= X86_REX_Prefix.REX_X;
				if ((opcode & 0x04) != 0)
					insn.RexPrefix |= X86_REX_Prefix.REX_R;
				if ((opcode & 0x08) != 0)
					insn.RexPrefix |= X86_REX_Prefix.REX_W;
				return true;
			} else if (opcode == 0x26) {
				insn.Prefix |= X86_Prefix.ES;
				return true;
			} else if (opcode == 0x2e) {
				insn.Prefix |= X86_Prefix.CS;
				return true;
			} else if (opcode == 0x36) {
				insn.Prefix |= X86_Prefix.SS;
				return true;
			} else if (opcode == 0x3e) {
				insn.Prefix |= X86_Prefix.DS;
				return true;
			} else if (opcode == 0x64) {
				insn.Prefix |= X86_Prefix.FS;
				return true;
			} else if (opcode == 0x65) {
				insn.Prefix |= X86_Prefix.GS;
				return true;
			} else if (opcode == 0x66) {
				insn.Prefix |= X86_Prefix.DATA;
				return true;
			} else if (opcode == 0x67) {
				insn.Prefix |= X86_Prefix.ADDR;
				return true;
			} else if (opcode == 0xf0) {
				insn.Prefix |= X86_Prefix.LOCK;
				return true;
			} else if (opcode == 0xf2) {
				insn.Prefix |= X86_Prefix.REPNZ;
				return true;
			} else if (opcode == 0xf3) {
				insn.Prefix |= X86_Prefix.REPZ;
				return true;
			}

			return false;
		}

		protected int DecodeRegister (int register)
		{
			if (Is64BitMode) {
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
			} else {
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
		}

		protected void DecodeModRM (X86_Instruction insn, TargetReader reader)
		{
			insn.ModRM = new ModRM (insn, reader.ReadByte ());

			if (Is64BitMode && (insn.ModRM.Mod == 0) && ((insn.ModRM.R_M & 0x07) == 0x06)) {
				Console.WriteLine ("IP RELATIVE!");
				insn.IsIpRelative = true;
			}
		}

		protected void OneByteOpcode (X86_Instruction insn, TargetReader reader, byte opcode)
		{
			Console.WriteLine ("ONE BYTE OPCODE: {0:x}", opcode);

			if (OneByte_Has_ModRM [opcode] != 0)
				DecodeModRM (insn, reader);

			if ((opcode >= 0x70) && (opcode <= 0x7f)) {
				insn.CallTarget = insn.Address + reader.ReadByte () + 2;
				insn.Type = InstructionType.ConditionalJump;
			} else if ((opcode >= 0xe0) && (opcode <= 0xe3)) {
				insn.CallTarget = insn.Address + reader.ReadByte () + 2;
				insn.Type = InstructionType.ConditionalJump;
			} else if ((opcode == 0xe8) || (opcode == 0xe9)) {
				if ((insn.RexPrefix & X86_REX_Prefix.REX_W) != 0) {
					long offset = reader.BinaryReader.ReadInt32 ();
					long long_target = insn.Address.Address + offset + 5;
					insn.CallTarget = new TargetAddress (
						insn.Address.Domain, long_target);
				} else if ((insn.Prefix & X86_Prefix.ADDR) != 0) {
					short offset = reader.BinaryReader.ReadInt16 ();
					int short_target = (short)insn.Address.Address + offset + 3;
					insn.CallTarget = new TargetAddress (
						insn.Address.Domain, short_target);
				} else {
					int offset = reader.BinaryReader.ReadInt32 ();
					insn.CallTarget = insn.Address + offset + 5;
				}
				insn.Type = (opcode == 0xe8) ?
					InstructionType.Call : InstructionType.Jump;
			} else if (opcode == 0xeb) {
				insn.CallTarget = insn.Address + reader.ReadByte () + 2;
				insn.Type = InstructionType.Jump;
			} else if (opcode == 0xff) {
				DecodeGroup5 (insn, reader);
			}

			Console.WriteLine ("ONE BYTE OPCODE DONE: {0:x} {1} {2}", opcode,
					   insn.Type, insn.CallTarget);
		}

		protected void TwoByteOpcode (X86_Instruction insn, TargetReader reader)
		{
			byte opcode = reader.ReadByte ();

			Console.WriteLine ("TWO BYTE OPCODE: {0:x}", opcode);

			if (TwoByte_Has_ModRM [opcode] != 0)
				DecodeModRM (insn, reader);

			if ((opcode >= 0x80) && (opcode <= 0x8f)) {
				if ((insn.RexPrefix & X86_REX_Prefix.REX_W) != 0) {
					long offset = reader.BinaryReader.ReadInt32 ();
					long long_target = insn.Address.Address + offset + 5;
					insn.CallTarget = new TargetAddress (
						insn.Address.Domain, long_target);
				} else if ((insn.Prefix & X86_Prefix.ADDR) != 0) {
					short offset = reader.BinaryReader.ReadInt16 ();
					int short_target = (short)insn.Address.Address + offset + 3;
					insn.CallTarget = new TargetAddress (
						insn.Address.Domain, short_target);
				} else {
					int offset = reader.BinaryReader.ReadInt32 ();
					insn.CallTarget = insn.Address + offset + 5;
				}
				insn.Type = InstructionType.ConditionalJump;
			}
		}

		protected void DecodeGroup5 (X86_Instruction insn, TargetReader reader)
		{
			if ((insn.ModRM.Reg == 2) || (insn.ModRM.Reg == 3))
				insn.Type = InstructionType.IndirectCall;
			else if ((insn.ModRM.Reg == 4) || (insn.ModRM.Reg == 5))
				insn.Type = InstructionType.IndirectJump;
			else
				return;

			Console.WriteLine ("GROUP 5: {0}", insn.ModRM);

			int displacement = 0;
			bool dereference_addr;

			int register;
			int index_register = -1;

			if ((insn.ModRM.Reg == 5) || (insn.ModRM.Reg == 13)) {
				/* Special meaning in mod == 00 */
				if (insn.ModRM.Mod == 0) {
					if (Is64BitMode) {
						displacement = reader.BinaryReader.ReadInt32 ();
						register = (int) X86_64_Register.RIP;
						insn.IsIpRelative = true;
					} else {
						insn.CallTarget = reader.ReadAddress ();
						return;
					}
				} else {
					register = DecodeRegister (insn.ModRM.Reg);
				}
			} else if ((insn.ModRM.Reg == 4) || (insn.ModRM.Reg == 12)) {
				/* Activate SIB byte if mod != 11 */
				if (insn.ModRM.Mod != 3) {
					insn.SIB = new SIB (insn, reader.ReadByte ());
					Console.WriteLine (insn.SIB);

					if ((insn.ModRM.Mod == 0) &&
					    ((insn.SIB.Base == 5) || (insn.SIB.Base == 13))) {
						displacement = reader.BinaryReader.ReadInt32 ();
						insn.CallTarget = new TargetAddress (
							reader.AddressDomain, displacement);
						return;
					}

					if (insn.SIB.Index != 4) {
						index_register = DecodeRegister (insn.SIB.Index);
					}

					register = DecodeRegister (insn.SIB.Base);
				} else {
					register = DecodeRegister (insn.ModRM.Reg);
				}
			} else {
				register = DecodeRegister (insn.ModRM.Reg);
			}

			if (insn.ModRM.Mod == 0) {
				dereference_addr = true;
			} else if (insn.ModRM.Mod == 1) {
				displacement = reader.ReadByte ();
				dereference_addr = true;
			} else if (insn.ModRM.Mod == 2) {
				displacement = reader.BinaryReader.ReadInt32 ();
				dereference_addr = true;
			} else if (insn.ModRM.Mod == 3) {
				displacement = 0;
				dereference_addr = false;
			} else {
				// Can never happen
				throw new InvalidOperationException ();
			}

			insn.Register = register;
			insn.IndexRegister = index_register;
			insn.Displacement = displacement;
			insn.DereferenceAddress = dereference_addr;

			Console.WriteLine ("GROUP 5 DONE: {0} {1} {2} {3}",
					   register, index_register, displacement, dereference_addr);
		}

		protected void DecodeInstruction (TargetReader reader, TargetAddress address)
		{
			Console.WriteLine ("DECODE INSN: {0} {1}", address,
					   reader.BinaryReader.HexDump ());

			X86_Instruction insn = new X86_Instruction (address);

			while (CheckPrefix (insn, reader))
				reader.Offset++;

			byte opcode = reader.ReadByte ();
			Console.WriteLine ("DECODE INSN #1: {0} {1} {2:x}",
					   insn.RexPrefix, insn.Prefix, opcode);

			if (opcode == 0x0f)
				TwoByteOpcode (insn, reader);
			else
				OneByteOpcode (insn, reader, opcode);

			if (insn.Type != InstructionType.Unknown)
				insn.SetInstructionSize ((int) reader.Offset);

			Console.WriteLine ("DONE DECODING INSTRUCTION: {0} {1} {2} {3} {4}",
					   insn.Type, insn.Prefix, insn.RexPrefix, insn.IsIpRelative,
					   insn.CallTarget);
		}
	}
}
