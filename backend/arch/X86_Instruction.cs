using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal abstract class X86_Instruction : Instruction
	{
		public const int MaxInstructionLength = 15;

		public abstract bool Is64BitMode {
			get;
		}

		protected X86_Instruction (Opcodes_X86 opcodes, TargetAddress address)
		{
			this.Opcodes = opcodes;
			this.address = address;
		}

		protected readonly Opcodes_X86 Opcodes;

		public X86_Prefix Prefix;
		public X86_REX_Prefix RexPrefix;

		public X86_ModRM ModRM;
		public X86_SIB SIB;

		/* For IndirectCall and IndirectJump */
		public int Register;
		public int IndexRegister;
		public int Displacement;
		public bool DereferenceAddress;

		readonly TargetAddress address;
		Type type = Type.Unknown;

		public override TargetAddress Address {
			get { return address; }
		}

		public override Type InstructionType {
			get { return type; }
		}

		public override bool IsIpRelative {
			get {
				switch (type) {
				case Type.ConditionalJump:
				case Type.IndirectCall:
				case Type.Call:
				case Type.IndirectJump:
				case Type.Jump:
					return true;

				default:
					return is_ip_relative;
				}
			}
		}

		public override bool HasInstructionSize {
			get { return has_insn_size; }
		}

		public override int InstructionSize {
			get {
				if (!has_insn_size)
					throw new InvalidOperationException ();

				return insn_size;
			}
		}

		public override byte[] Code {
			get {
				if (!has_insn_size)
					throw new InvalidOperationException ();

				return code;
			}
		}

		bool is_ip_relative;
		bool has_insn_size;
		int insn_size;
		byte[] code;

		public TargetAddress CallTarget = TargetAddress.Null;

		[Flags]
		public enum X86_Prefix
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
		public enum X86_REX_Prefix
		{
			REX_B	= 1,
			REX_X	= 2,
			REX_R	= 4,
			REX_W	= 8
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

		public class X86_ModRM
		{
			public readonly int Mod;
			public readonly int Reg;
			public readonly int R_M;

			public X86_ModRM (X86_Instruction insn, byte modrm)
			{
				Mod = (modrm & 0xc0) >> 6;
				Reg = (modrm & 0x38) >> 3;
				R_M = (modrm & 0x7);

				if ((insn.RexPrefix & X86_REX_Prefix.REX_R) != 0)
					Reg |= 0x08;
				if ((insn.RexPrefix & X86_REX_Prefix.REX_B) != 0)
					R_M |= 0x08;
			}

			public override string ToString ()
			{
				return String.Format ("ModRM (mod={0}, reg={1}, r/m={2})",
						      format_2_bits (Mod), format_4_bits (Reg),
						      format_4_bits (R_M));
			}
		}

		public class X86_SIB
		{
			public readonly int Scale;
			public readonly int Index;
			public readonly int Base;

			public X86_SIB (X86_Instruction insn, byte sib)
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

		bool CheckPrefix (TargetReader reader)
		{
			byte opcode = reader.PeekByte ();
			if ((opcode >= 0x40) && (opcode <= 0x4f)) {
				if (!Is64BitMode)
					return false;

				if ((opcode & 0x01) != 0)
					RexPrefix |= X86_REX_Prefix.REX_B;
				if ((opcode & 0x02) != 0)
					RexPrefix |= X86_REX_Prefix.REX_X;
				if ((opcode & 0x04) != 0)
					RexPrefix |= X86_REX_Prefix.REX_R;
				if ((opcode & 0x08) != 0)
					RexPrefix |= X86_REX_Prefix.REX_W;
				return true;
			} else if (opcode == 0x26) {
				Prefix |= X86_Prefix.ES;
				return true;
			} else if (opcode == 0x2e) {
				Prefix |= X86_Prefix.CS;
				return true;
			} else if (opcode == 0x36) {
				Prefix |= X86_Prefix.SS;
				return true;
			} else if (opcode == 0x3e) {
				Prefix |= X86_Prefix.DS;
				return true;
			} else if (opcode == 0x64) {
				Prefix |= X86_Prefix.FS;
				return true;
			} else if (opcode == 0x65) {
				Prefix |= X86_Prefix.GS;
				return true;
			} else if (opcode == 0x66) {
				Prefix |= X86_Prefix.DATA;
				return true;
			} else if (opcode == 0x67) {
				Prefix |= X86_Prefix.ADDR;
				return true;
			} else if (opcode == 0xf0) {
				Prefix |= X86_Prefix.LOCK;
				return true;
			} else if (opcode == 0xf2) {
				Prefix |= X86_Prefix.REPNZ;
				return true;
			} else if (opcode == 0xf3) {
				Prefix |= X86_Prefix.REPZ;
				return true;
			}

			return false;
		}

		protected abstract int DecodeRegister (int register);

		protected void DecodeModRM (TargetReader reader)
		{
			ModRM = new X86_ModRM (this, reader.ReadByte ());

			if (Is64BitMode && (ModRM.Mod == 0) && ((ModRM.R_M & 0x07) == 0x06)) {
				is_ip_relative = true;
			}
		}

		protected void OneByteOpcode (TargetReader reader, byte opcode)
		{
			if (OneByte_Has_ModRM [opcode] != 0)
				DecodeModRM (reader);

			if ((opcode >= 0x70) && (opcode <= 0x7f)) {
				CallTarget = Address + reader.ReadByte () + 2;
				type = Type.ConditionalJump;
			} else if ((opcode >= 0xe0) && (opcode <= 0xe3)) {
				CallTarget = Address + reader.ReadByte () + 2;
				type = Type.ConditionalJump;
			} else if ((opcode == 0xe8) || (opcode == 0xe9)) {
				if ((RexPrefix & X86_REX_Prefix.REX_W) != 0) {
					long offset = reader.BinaryReader.ReadInt32 ();
					long long_target = Address.Address + offset + 5;
					CallTarget = new TargetAddress (
						Address.Domain, long_target);
				} else if ((Prefix & X86_Prefix.ADDR) != 0) {
					short offset = reader.BinaryReader.ReadInt16 ();
					int short_target = (short)Address.Address + offset + 3;
					CallTarget = new TargetAddress (
						Address.Domain, short_target);
				} else {
					int offset = reader.BinaryReader.ReadInt32 ();
					CallTarget = Address + offset + 5;
				}
				type = (opcode == 0xe8) ? Type.Call : Type.Jump;
			} else if (opcode == 0xeb) {
				CallTarget = Address + reader.ReadByte () + 2;
				type = Type.Jump;
			} else if (opcode == 0xff) {
				DecodeGroup5 (reader);
			}
		}

		protected void TwoByteOpcode (TargetReader reader)
		{
			byte opcode = reader.ReadByte ();

			if (TwoByte_Has_ModRM [opcode] != 0)
				DecodeModRM (reader);

			if ((opcode >= 0x80) && (opcode <= 0x8f)) {
				if ((RexPrefix & X86_REX_Prefix.REX_W) != 0) {
					long offset = reader.BinaryReader.ReadInt32 ();
					long long_target = Address.Address + offset + 5;
					CallTarget = new TargetAddress (
						Address.Domain, long_target);
				} else if ((Prefix & X86_Prefix.ADDR) != 0) {
					short offset = reader.BinaryReader.ReadInt16 ();
					int short_target = (short)Address.Address + offset + 3;
					CallTarget = new TargetAddress (
						Address.Domain, short_target);
				} else {
					int offset = reader.BinaryReader.ReadInt32 ();
					CallTarget = Address + offset + 5;
				}
				type = Type.ConditionalJump;
			}
		}

		protected void DecodeGroup5 (TargetReader reader)
		{
			if ((ModRM.Reg == 2) || (ModRM.Reg == 3))
				type = Type.IndirectCall;
			else if ((ModRM.Reg == 4) || (ModRM.Reg == 5))
				type = Type.IndirectJump;
			else
				return;

			int displacement = 0;
			bool dereference_addr;

			int register;
			int index_register = -1;

			if ((ModRM.R_M == 5) || (ModRM.R_M == 13)) {
				/* Special meaning in mod == 00 */
				if (ModRM.Mod == 0) {
					if (Is64BitMode) {
						displacement = reader.BinaryReader.ReadInt32 ();
						register = -1;
						is_ip_relative = true;
					} else {
						CallTarget = reader.ReadAddress ();
						return;
					}
				} else {
					register = DecodeRegister (ModRM.R_M);
				}
			} else if ((ModRM.R_M == 4) || (ModRM.R_M == 12)) {
				/* Activate SIB byte if mod != 11 */
				if (ModRM.Mod != 3) {
					SIB = new X86_SIB (this, reader.ReadByte ());

					if ((ModRM.Mod == 0) &&
					    ((SIB.Base == 5) || (SIB.Base == 13))) {
						displacement = reader.BinaryReader.ReadInt32 ();
						CallTarget = new TargetAddress (
							reader.AddressDomain, displacement);
						return;
					}

					if (SIB.Index != 4) {
						index_register = DecodeRegister (SIB.Index);
					}

					register = DecodeRegister (SIB.Base);
				} else {
					register = DecodeRegister (ModRM.R_M);
				}
			} else {
				register = DecodeRegister (ModRM.R_M);
			}

			if (ModRM.Mod == 0) {
				dereference_addr = true;
			} else if (ModRM.Mod == 1) {
				displacement = reader.ReadByte ();
				dereference_addr = true;
			} else if (ModRM.Mod == 2) {
				displacement = reader.BinaryReader.ReadInt32 ();
				dereference_addr = true;
			} else if (ModRM.Mod == 3) {
				displacement = 0;
				dereference_addr = false;
			} else {
				// Can never happen
				throw new InvalidOperationException ();
			}

			Register = register;
			IndexRegister = index_register;
			Displacement = displacement;
			DereferenceAddress = dereference_addr;
		}

		public override TargetAddress GetEffectiveAddress (TargetMemoryAccess memory)
		{
			if (!CallTarget.IsNull)
				return CallTarget;

			Registers regs = memory.GetRegisters ();

			long effective_displacement = Displacement;
			if (IndexRegister >= 0) {
				long index = regs [IndexRegister].GetValue ();
				index *= 1 << SIB.Scale;
				effective_displacement += index;
			}

			TargetAddress effective_address;
			if (is_ip_relative)
				effective_address = Address + InstructionSize;
			else
				effective_address = new TargetAddress (
					memory.AddressDomain, regs [Register].GetValue ());

			effective_address += effective_displacement;

			if (DereferenceAddress)
				effective_address = memory.ReadAddress (effective_address);

			return effective_address;
		}

		public static X86_Instruction DecodeInstruction (Opcodes_X86 opcodes,
								 TargetMemoryAccess memory,
								 TargetAddress address)
		{
			try {
				X86_Instruction insn;
				if (memory.TargetMemoryInfo.TargetAddressSize == 8)
					insn = new Instruction_X86_64 (opcodes, address);
				else
					insn = new Instruction_I386 (opcodes, address);
				insn.DoDecodeInstruction (memory, address);
				return insn;
			} catch {
				return null;
			}
		}

		protected void DoDecodeInstruction (TargetMemoryAccess memory,
						    TargetAddress address)
		{
			TargetReader reader = new TargetReader (
				memory.ReadMemory (address, MaxInstructionLength));

			while (CheckPrefix (reader))
				reader.Offset++;

			byte opcode = reader.ReadByte ();

			if (opcode == 0x0f)
				TwoByteOpcode (reader);
			else
				OneByteOpcode (reader, opcode);

			if (InstructionType != Type.Unknown) {
				insn_size = (int) reader.Offset;
				has_insn_size = true;
			} else {
				insn_size = Opcodes.Disassembler.GetInstructionSize (memory, address);
				has_insn_size = true;
			}

			if (has_insn_size) {
				code = new byte [insn_size];
				Array.Copy (reader.Contents, 0, code, 0, insn_size);
			}
		}

		public override string ToString ()
		{
			return String.Format ("Instruction ({0}:{1}:{2})", Address, InstructionType,
					      insn_size);
		}
	}
}
