using System;
using System.Collections;

namespace Mono.Debugger.Backends
{
	internal class DwarfFrameReader
	{
		protected readonly Bfd bfd;
		protected readonly TargetBlob blob;
		protected readonly bool is_ehframe;
		protected readonly long vma;
		protected CIE cie_list;

		public DwarfFrameReader (Bfd bfd, TargetBlob blob, long vma,
					 bool is_ehframe)
		{
			this.bfd = bfd;
			this.blob = blob;
			this.vma = vma;
			this.is_ehframe = is_ehframe;
		}

		protected CIE find_cie (long offset)
		{
			for (CIE cie = cie_list; cie != null; cie = cie.Next) {
				if (cie.Offset == offset)
					return cie;
			}

			CIE new_cie = new CIE (this, offset, cie_list);
			cie_list = new_cie;
			return cie_list;
		}

		public StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess target,
					       Architecture arch)
		{
			if (frame.TargetAddress.IsNull)
				return null;

			TargetAddress address = frame.TargetAddress;

			DwarfBinaryReader reader = new DwarfBinaryReader (bfd, blob, false);

			while (reader.Position < reader.Size) {
				long length = reader.ReadInitialLength ();
				if (length == 0)
					break;
				long end_pos = reader.Position + length;

				long cie_pointer = reader.ReadOffset ();
				bool is_cie;
				if (is_ehframe)
					is_cie = cie_pointer == 0;
				else
					is_cie = cie_pointer == -1;

				if (is_cie) {
					reader.Position = end_pos;
					continue;
				}

				if (is_ehframe)
					cie_pointer = reader.Position - cie_pointer -
						target.TargetInfo.TargetAddressSize;

				CIE cie = find_cie (cie_pointer);

				long initial = reader.ReadAddress ();
				long range = reader.ReadAddress ();

				TargetAddress start = new TargetAddress (
					target.TargetInfo.AddressDomain, initial);

				if ((address < start) || (address > start + range)) {
					reader.Position = end_pos;
					continue;
				}

				Entry fde = new Entry (cie, start, address);
				fde.Read (reader, end_pos);
				return fde.Unwind (frame, target, arch);
			}

			return null;
		}

		public long ReadEncodedValue (DwarfBinaryReader reader, int encoding)
		{
			if ((encoding & 0x0f) == 0x00)
				encoding |= (byte) DW_EH_PE.udata4;

			long base_addr = 0;
			switch (encoding & 0x70) {
			case (byte) DW_EH_PE.pcrel:
				base_addr = vma + reader.Position;
				break;
			}

			switch (encoding & 0x0f) {
			case (byte) DW_EH_PE.udata4:
				return base_addr + reader.ReadUInt32 ();

			case (byte) DW_EH_PE.sdata4:
				return base_addr + reader.ReadInt32 ();

			default:
				throw new DwarfException (
					reader.Bfd, "Unknown encoding `{0:x}' in CIE",
					encoding);
			}
		}

		protected enum DW_CFA : byte
		{
			// First byte
			advance_loc		= 0x01,
			offset			= 0x02,
			restore			= 0x03,

			// Second byte
			nop			= 0x00,
			set_loc			= 0x01,
			advance_loc1		= 0x02,
			advance_loc2		= 0x03,
			advance_loc4		= 0x04,
			offset_extended		= 0x05,
			restore_extended	= 0x06,
			undefined		= 0x07,
			same_value		= 0x08,
			register		= 0x09,
			remember_state		= 0x0a,
			restore_state		= 0x0b,
			def_cfa			= 0x0c,
			def_cfa_register	= 0x0d,
			def_cfa_offset		= 0x0e,
			def_cfa_expression	= 0x0f,
			cfa_expression		= 0x10,
			offset_extended_sf	= 0x11,
			def_cfa_sf		= 0x12,
			def_cfa_offset_sf	= 0x13,

			// GNU extensions
			gnu_args_size		= 0x2e
		}

		[Flags]
		protected enum DW_EH_PE : byte
		{
			absptr	= 0x00,
			omit	= 0xff,

			uleb128	= 0x01,
			udata2	= 0x02,
			udata4	= 0x03,
			udata8	= 0x04,
			sleb128	= 0x09,
			sdata2	= 0x0a,
			sdata4	= 0x0b,
			sdata8	= 0x0c,
			signed	= 0x08,

			pcrel	= 0x10,
			textrel	= 0x20,
			datarel	= 0x30,
			funcrel	= 0x40,
			aligned	= 0x50,

			indirect= 0x80
		}

		protected enum State
		{
			Undefined,
			SameValue,
			Offset,
			Register
		}

		protected struct Column
		{
			public State State;
			public int Register;
			public int Offset;

			public Column (State state)
			{
				this.State = state;
				this.Register = 0;
				this.Offset = 0;
			}

			public override string ToString ()
			{
				return String.Format ("[{0}:{1}:{2:x}]", State, Register, Offset);
			}
		}

		protected class Entry
		{
			public readonly CIE cie;
			protected TargetAddress current_address;
			protected TargetAddress address;
			Column[] columns;

			public Entry (CIE cie)
			{
				this.cie = cie;
				this.current_address = TargetAddress.Null;
				this.address = TargetAddress.Null;
				this.columns = cie.Columns;
			}

			public Entry (CIE cie, TargetAddress initial_location,
				      TargetAddress address)
			{
				this.cie = cie;
				this.current_address = initial_location;
				this.address = address;
				this.columns = new Column [cie.Columns.Length];
				cie.Columns.CopyTo (columns, 0);
			}

			public Column[] Columns {
				get { return columns; }
			}

			public void Read (DwarfBinaryReader reader, long end_pos)
			{
				while (reader.Position < end_pos) {
					byte first = reader.ReadByte ();
					int opcode = first >> 6;
					int low = first & 0x3f;

					if (opcode == (int) DW_CFA.offset) {
						int offset = reader.ReadLeb128 ();
						offset *= cie.DataAlignment;

						columns [low + 1].State = State.Offset;
						columns [low + 1].Register = 0;
						columns [low + 1].Offset = offset;
						continue;
					} else if (opcode == (int) DW_CFA.advance_loc) {
						current_address += low;
						if (current_address > address)
							return;
						continue;
					} else if (opcode != 0) {
						continue;
					}

					switch ((DW_CFA) low) {
					case DW_CFA.nop:
						break;
					case DW_CFA.def_cfa:
						columns [0].State = State.Register;
						columns [0].Register = reader.ReadLeb128 ();
						columns [0].Offset = reader.ReadLeb128 ();
						break;
					case DW_CFA.def_cfa_register:
						columns [0].State = State.Register;
						columns [0].Register = reader.ReadLeb128 ();
						break;
					case DW_CFA.def_cfa_offset:
						columns [0].Offset = reader.ReadLeb128 ();
						break;
					case DW_CFA.gnu_args_size:
						// Ignored.
						reader.ReadLeb128 ();
						break;
					default:
						break;
					}
				}
			}

			int GetArchRegister (int index)
			{
				return cie.Architecture.DwarfFrameRegisterMap [index];
			}

			Register GetRegister (Registers regs, int index)
			{
				return regs [GetArchRegister (index)];
			}

			long GetRegisterValue (Registers regs, int reg, Column column)
			{
				int index = GetArchRegister (column.Register + 3);
				long value = regs [index].GetValue () + column.Offset;

				regs [GetArchRegister (reg)].SetValue (TargetAddress.Null, value);
				return value;
			}

			void GetValue (TargetMemoryAccess target, Registers regs,
				       TargetAddress cfa, int reg, Column column)
			{
				switch (column.State) {
				case State.Register: {
					GetRegisterValue (regs, reg, column);
					break;
				}

				case State.SameValue:
					regs [GetArchRegister (reg)].Valid = true;
					break;

				case State.Undefined:
					break;

				case State.Offset: {
					TargetAddress addr = cfa + column.Offset;
					long value = target.ReadAddress (addr).Address;
					regs [GetArchRegister (reg)].SetValue (address, value);
					break;
				}

				default:
					throw new NotSupportedException ();
				}
			}

			void SetRegisters (Registers regs, TargetMemoryAccess target,
					   Architecture arch, Column[] columns)
			{
				long cfa_addr = GetRegisterValue (regs, 1, columns [0]);
				TargetAddress cfa = new TargetAddress (
					arch.AddressDomain, cfa_addr);

				for (int i = 1; i < columns.Length; i++) {
					GetValue (target, regs, cfa, i+2, columns [i]);
				}
			}

			public StackFrame Unwind (StackFrame frame, TargetMemoryAccess target,
						  Architecture arch)
			{
				Registers old_regs = frame.Registers;

				Registers regs = arch.CopyRegisters (old_regs);

				SetRegisters (regs, target, arch, columns);

				Register eip = GetRegister (regs, 0);
				Register esp = GetRegister (regs, 1);
				Register ebp = GetRegister (regs, 2);

				if (!eip.Valid || !esp.Valid)
					return null;

				TargetAddress address = new TargetAddress (
					arch.AddressDomain, eip.Value);
				TargetAddress stack = new TargetAddress (
					arch.AddressDomain, esp.Value);

				TargetAddress frame_addr = TargetAddress.Null;
				if (ebp.Valid)
					frame_addr = new TargetAddress (
						arch.AddressDomain, ebp.Value);

				return arch.CreateFrame (
					frame.Thread, address, stack, frame_addr, regs, true);
			}
		}

		protected class CIE
		{
			DwarfFrameReader frame;
			long offset;
			CIE next;

			int code_alignment;
			int data_alignment;
			int return_register;
			// bool has_z_augmentation;
			byte encoding = (byte) DW_EH_PE.udata4;
			Column[] columns;

			public CIE (DwarfFrameReader frame, long offset, CIE next)
			{
				this.frame = frame;
				this.offset = offset;
				this.next = next;

				DwarfBinaryReader reader = new DwarfBinaryReader (
					frame.bfd, frame.blob, false);
				read_cie (reader);
			}

			public CIE Next {
				get { return next; }
			}

			public Architecture Architecture {
				get { return frame.bfd.Architecture; }
			}

			public long Offset {
				get { return offset; }
			}

			public int CodeAlignment {
				get { return code_alignment; }
			}

			public int DataAlignment {
				get { return data_alignment; }
			}

			public int ReturnRegister {
				get { return return_register; }
			}

			public byte Encoding {
				get { return encoding; }
			}

			public Column[] Columns {
				get { return columns; }
			}

			void read_cie (DwarfBinaryReader reader)
			{
				long length = reader.ReadInitialLength ();
				long end_pos = reader.Position + length;
				int id = reader.ReadInt32 ();

				bool is_cie;
				if (frame.is_ehframe)
					is_cie = id == 0;
				else
					is_cie = id == -1;
				if (!is_cie)
					throw new InvalidOperationException ();

				int version = reader.ReadByte ();
				if (version != 1)
					throw new DwarfException (
						reader.Bfd, "Unknown version {0} in CIE",
						version);

				string augmentation = reader.ReadString ();

				if (augmentation.StartsWith ("eh")) {
					reader.ReadAddress ();
					augmentation = augmentation.Substring (2);
				}

				code_alignment = reader.ReadLeb128 ();
				data_alignment = reader.ReadSLeb128 ();
				return_register = reader.ReadByte ();

				for (int pos = 0; pos < augmentation.Length; pos++) {
					if (augmentation [pos] == 'z') {
						reader.ReadLeb128 ();
						// has_z_augmentation = true;
						continue;
					}

					if (augmentation [pos] == 'L')
						continue;
					else if (augmentation [pos] == 'R') {
						encoding = reader.ReadByte ();
						continue;
					}
					else if (augmentation [pos] == 'P') {
						continue;
					}

					throw new DwarfException (
						reader.Bfd, "Unknown augmentation `{0}' in CIE",
						augmentation[pos]);
				}

				columns = new Column [return_register + 2];
				for (int i = 0; i < columns.Length; i++)
					columns [i] = new Column (State.Undefined);

				Entry entry = new Entry (this);
				entry.Read (reader, end_pos);

				reader.Position = end_pos;
			}
		}

		public override string ToString ()
		{
			return String.Format ("DwarfFrameReader ({0}:{1}:{2})",
					      bfd.FileName, is_ehframe, blob.Size);
		}
	}
}
