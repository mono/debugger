using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Architecture
{
	public class DwarfReader : ISymbolTable, IDisposable
	{
		protected BfdSymbolTable bfd;
		protected string filename;
		bool is64bit;
		byte address_size;

		WeakReference weak_info_reader;
		WeakReference weak_abbrev_reader;
		WeakReference weak_line_reader;
		WeakReference weak_aranges_reader;
		WeakReference weak_str_reader;

		ArrayList ranges;
		Hashtable compile_unit_hash;

		protected class DwarfException : Exception
		{
			public DwarfException (DwarfReader reader, string message)
				: base (String.Format ("{0}: {1}", reader.FileName, message))
			{ }

			public DwarfException (DwarfReader reader, string message, Exception inner)
				: base (String.Format ("{0}: {1}", reader.FileName, message), inner)
			{ }
		}

		public DwarfReader (BfdSymbolTable bfd)
		{
			this.bfd = bfd;
			this.filename = bfd.FileName;

			DwarfBinaryReader reader = DebugInfoReader;

			long length = reader.ReadInitialLength (out is64bit);
			long stop = reader.Position + length;
			int version = reader.ReadInt16 ();
			if (version < 2)
				throw new DwarfException (this, String.Format (
					"Wrong DWARF version: {0}", version));

			reader.ReadOffset ();
			address_size = reader.ReadByte ();

			if ((address_size != 4) && (address_size != 8))
				throw new DwarfException (this, String.Format (
					"Unknown address size: {0}", address_size));

			compile_unit_hash = new Hashtable ();

			Console.WriteLine ("Reading aranges table ....");

			read_aranges ();

			Console.WriteLine ("Done reading aranges table");
		}

		protected class CompileUnitBlock
		{
			public readonly DwarfReader dwarf;
			public readonly long offset;

			ArrayList compile_units;

			public bool Lookup (ITargetLocation target, out IMethod method)
			{
				method = null;
				foreach (CompilationUnit comp_unit in compile_units) {
					ISymbolTable symtab = comp_unit.SymbolTable;

					if (!symtab.Lookup (target, out method))
						continue;
					return true;
				}
				return false;
			}

			public CompileUnitBlock (DwarfReader dwarf, long start)
			{
				this.dwarf = dwarf;
				this.offset = start;

				DwarfBinaryReader reader = dwarf.DebugInfoReader;
				reader.Position = start;
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				if (version < 2)
					throw new DwarfException (dwarf, String.Format (
						"Wrong DWARF version: {0}", version));

				reader.ReadOffset ();
				address_size = reader.ReadByte ();
				reader.Position = start;

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (dwarf, String.Format (
						"Unknown address size: {0}", address_size));

				compile_units = new ArrayList ();

				while (reader.Position < stop) {
					CompilationUnit comp_unit = new CompilationUnit (dwarf, reader);
					compile_units.Add (comp_unit);
				}
			}
		}

		public ISymbolTable SymbolTable {
			get {
				return this;
			}
		}

		bool ISymbolContainer.IsContinuous {
			get {
				return false;
			}
		}

		ITargetLocation ISymbolContainer.StartAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		ITargetLocation ISymbolContainer.EndAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		bool ISymbolTable.Lookup (ITargetLocation target, out IMethod method)
		{
			method = null;
			long address = target.Address;
			foreach (RangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address >= range.EndAddress))
					continue;

				CompileUnitBlock block = (CompileUnitBlock) compile_unit_hash [range.Offset];
				if (block == null) {
					block = new CompileUnitBlock (this, range.Offset);
					compile_unit_hash.Add (range.Offset, block);
				}

				return block.Lookup (target, out method);
			}

			return false;
		}

		bool ISymbolTable.Lookup (ITargetLocation target, out ISourceLocation source,
					  out IMethod method)
		{
			source = null;
			if (!ISymbolTable.Lookup (target, out method))
				return false;

			source = method.Lookup (target);
			return true;
		}

		protected struct RangeEntry
		{
			public readonly long Offset;
			public readonly long StartAddress;
			public readonly long EndAddress;

			public RangeEntry (long offset, long address, long size)
			{
				this.Offset = offset;
				this.StartAddress = address;
				this.EndAddress = address + size;
			}
		}

		void read_aranges ()
		{
			DwarfBinaryReader reader = DebugArangesReader;

			ranges = new ArrayList ();

			while (!reader.IsEof) {
				long start = reader.Position;
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long offset = reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				int segment_size = reader.ReadByte ();

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (this, String.Format (
						"Unknown address size: {0}", address_size));
				if (segment_size != 0)
					throw new DwarfException (this, "Segmented address mode not supported");

				if (version != 2)
					throw new DwarfException (this, String.Format (
						"Wrong version in .debug_aranges: {0}", version));

				if (AddressSize == 8)
					reader.Position = ((reader.Position+15) >> 4) * 16;
				else
					reader.Position = ((reader.Position+7) >> 3) * 8;

				while (reader.Position < stop) {
					long address = reader.ReadAddress ();
					long size = reader.ReadAddress ();

					if ((address == 0) && (size == 0))
						break;

					ranges.Add (new RangeEntry (offset, address, size));
				}
			}
		}

		DwarfBinaryReader get_reader (string section, ref WeakReference weak)
		{
			DwarfBinaryReader reader = null;
			if (weak != null) {
				try {
					reader = (DwarfBinaryReader) weak.Target;
				} catch {
					weak = null;
				}
			}
			if (reader != null)
				return reader;
			reader = new DwarfBinaryReader (this, section);
			weak = new WeakReference (reader);
			return reader;
		}

		public DwarfBinaryReader DebugInfoReader {
			get {
				return get_reader (".debug_info", ref weak_info_reader);
			}
		}

		public DwarfBinaryReader DebugAbbrevReader {
			get {
				return get_reader (".debug_abbrev", ref weak_abbrev_reader);
			}
		}

		public DwarfBinaryReader DebugLineReader {
			get {
				return get_reader (".debug_line", ref weak_line_reader);
			}
		}

		public DwarfBinaryReader DebugArangesReader {
			get {
				return get_reader (".debug_aranges", ref weak_aranges_reader);
			}
		}

		public DwarfBinaryReader DebugStrReader {
			get {
				return get_reader (".debug_str", ref weak_str_reader);
			}
		}

		public string FileName {
			get {
				return filename;
			}
		}

		public bool Is64Bit {
			get {
				return is64bit;
			}
		}

		public byte AddressSize {
			get {
				return address_size;
			}
		}

		static void debug (string message, params object[] args)
		{
			Console.WriteLine (String.Format (message, args));
		}

		public class DwarfBinaryReader
		{
			DwarfReader dwarf;
			byte[] contents;
			string section_name;
			byte address_size;
			bool is64bit;
			int pos;

			public DwarfBinaryReader (DwarfReader dwarf, string section_name)
			{
				this.section_name = section_name;
				this.dwarf = dwarf;

				contents = dwarf.bfd.GetSectionContents (section_name);
				if (contents == null)
					throw new DwarfException (dwarf, "Can't file DWARF 2 debugging info");
				address_size = dwarf.AddressSize;
				is64bit = dwarf.Is64Bit;

				debug ("Creating new `{0}' reader.", section_name);
			}

			public DwarfReader DwarfReader {
				get {
					return dwarf;
				}
			}

			public long Size {
				get {
					return contents.Length;
				}
			}

			public long Position {
				get {
					return pos;
				}

				set {
					pos = (int) value;
				}
			}

			public bool IsEof {
				get {
					return pos == contents.Length;
				}
			}

			public byte PeekByte (long pos)
			{
				return contents[pos];
			}

			public byte PeekByte ()
			{
				return contents[pos];
			}

			public byte ReadByte ()
			{
				return contents[pos++];
			}

			public short PeekInt16 (long pos)
			{
				return ((short) (contents[pos] | (contents[pos+1] << 8)));
			}

			public short ReadInt16 ()
			{
				short retval = PeekInt16 (pos);
				pos += 2;
				return retval;
			}

			public int PeekInt32 (long pos)
			{
				return (contents[pos] | (contents[pos+1] << 8) |
					(contents[pos+2] << 16) | (contents[pos+3] << 24));
			}

			public int ReadInt32 ()
			{
				int retval = PeekInt32 (pos);
				pos += 4;
				return retval;
			}

			public long PeekInt64 (long pos)
			{
				uint ret_low  = (uint) (contents[pos]           |
							(contents[pos+1] << 8)  |
							(contents[pos+2] << 16) |
							(contents[pos+3] << 24));
				uint ret_high = (uint) (contents[pos+4]         |
							(contents[pos+5] << 8)  |
							(contents[pos+6] << 16) |
							(contents[pos+7] << 24));
				return (long) ((((ulong) ret_high) << 32) | ret_low);
			}

			public long ReadInt64 ()
			{
				long retval = PeekInt64 (pos);
				pos += 8;
				return retval;
			}

			public long PeekOffset (long pos)
			{
				if (is64bit)
					return PeekInt64 (pos);
				else
					return PeekInt32 (pos);
			}

			public long PeekOffset (long pos, out int size)
			{
				if (is64bit) {
					size = 8;
					return PeekInt64 (pos);
				} else {
					size = 4;
					return PeekInt32 (pos);
				}
			}

			public long ReadOffset ()
			{
				if (is64bit)
					return ReadInt64 ();
				else
					return ReadInt32 ();
			}

			public long PeekAddress (long pos)
			{
				if (address_size == 8)
					return PeekInt64 (pos);
				else
					return PeekInt32 (pos);
			}

			public long ReadAddress ()
			{
				if (address_size == 8)
					return ReadInt64 ();
				else
					return ReadInt32 ();
			}

			public string PeekString (long pos)
			{
				int length = 0;
				while (contents[pos+length] != 0)
					length++;

				char[] retval = new char [length];
				for (int i = 0; i < length; i++)
					retval [i] = (char) contents[pos+i];

				return new String (retval);
			}

			public string ReadString ()
			{
				string retval = PeekString (pos);
				pos += retval.Length + 1;
				return retval;
			}

			public byte[] PeekBuffer (long offset, int size)
			{
				byte[] buffer = new byte [size];

				Array.Copy (contents, pos, buffer, 0, size);

				return buffer;
			}

			public byte[] ReadBuffer (int size)
			{
				byte[] buffer = new byte [size];

				Array.Copy (contents, pos, buffer, 0, size);
				pos += size;

				return buffer;
			}

			public int PeekLeb128 (long pos)
			{
				int ret = 0;
				int shift = 0;
				byte b;

				do {
					b = PeekByte (pos++);
				
					ret = ret | ((b & 0x7f) << shift);
					shift += 7;
				} while ((b & 0x80) == 0x80);

				return ret;
			}

			public int PeekLeb128 (long pos, out int size)
			{
				int ret = 0;
				int shift = 0;
				byte b;

				size = 0;
				do {
					b = PeekByte (pos + size);
					size++;
				
					ret = ret | ((b & 0x7f) << shift);
					shift += 7;
				} while ((b & 0x80) == 0x80);

				return ret;
			}

			public int ReadLeb128 ()
			{
				int size;
				int retval = PeekLeb128 (pos, out size);
				pos += size;
				return retval;
			}

			public long ReadInitialLength ()
			{
				bool is64bit;
				return ReadInitialLength (out is64bit);
			}

			public long ReadInitialLength (out bool is64bit)
			{
				long length = ReadInt32 ();

				if (length < 0xfffffff0) {
					is64bit = false;
					return length;
				} else if (length == 0xffffffff) {
					is64bit = true;
					return ReadInt64 ();
				} else
					throw new DwarfException (dwarf, String.Format (
						"Unknown initial length field {0:x}", length));
			}

			~DwarfBinaryReader ()
			{
				debug ("Finalizing `{0}' reader.", section_name);
			}
		}

		protected class DwarfSymbolTable : SymbolTable
		{
			DieCompileUnit comp_unit_die;

			public DwarfSymbolTable (DieCompileUnit comp_unit_die)
				: base (comp_unit_die)
			{
				this.comp_unit_die = comp_unit_die;
			}

			protected override ArrayList GetMethods ()
			{
				comp_unit_die.ReadLineNumbers ();

				ArrayList methods = new ArrayList ();

				foreach (Die child in comp_unit_die.Children) {
					DieSubprogram subprog = child as DieSubprogram;
					if ((subprog == null) || !subprog.IsContinuous)
						continue;

					NativeMethod native = new NativeMethod (
						subprog.Name, comp_unit_die.ImageFile,
						subprog.StartAddress, subprog.EndAddress);

					methods.Add (native);
				}

				return methods;
			}
		}

		public enum DwarfTag {
			array_type		= 0x01,
			class_type		= 0x02,
			enumeration_type	= 0x04,
			formal_parameter	= 0x05,
			member			= 0x0d,
			pointer_type		= 0x0f,
			compile_unit		= 0x11,
			structure_type		= 0x13,
			inheritance		= 0x1c,
			subrange_type		= 0x21,
			access_declaration	= 0x23,
			base_type		= 0x24,
			enumerator		= 0x28,
			subprogram		= 0x2e,
			variable		= 0x34
		}

		public enum DwarfAttribute {
			location	        = 0x02,
			name			= 0x03,
			byte_size		= 0x0b,
			stmt_list		= 0x10,
			low_pc			= 0x11,
			high_pc			= 0x12,
			language		= 0x13,
			comp_dir		= 0x1b,
			const_value		= 0x1c,
			lower_bound		= 0x22,
			producer		= 0x25,
			start_scope		= 0x2c,
			upper_bound		= 0x2f,
			accessibility		= 0x32,
			artificial		= 0x34,
			calling_convention	= 0x36,
			count			= 0x37,
			data_member_location	= 0x38,
			encoding		= 0x3e,
			external		= 0x3f,
			type			= 0x49,
			virtuality		= 0x4c,
			vtable_elem_location	= 0x4d
		}

		public enum DwarfForm {
			addr			= 0x01,
			block2			= 0x03,
			block4			= 0x04,
			data2			= 0x05,
			data4		        = 0x06,
			data8			= 0x07,
			cstring			= 0x08,
			block			= 0x09,
			block1			= 0x0a,
			data1			= 0x0b,
			flag			= 0x0c,
			sdata			= 0x0d,
			strp			= 0x0e,
			udata			= 0x0f,
			ref_addr		= 0x10,
			ref1			= 0x11,
			ref2			= 0x12,
			ref4			= 0x13,
			ref8			= 0x14,
			ref_udata		= 0x15
		}

		protected class LineNumberEngine
		{
			DwarfReader dwarf;
			DwarfBinaryReader reader;
			long offset;

			long length;
			int version;
			long header_length, data_offset, end_offset;
			byte minimum_insn_length;
			bool default_is_stmt;

			int line_base, line_range, opcode_base;
			int[] standard_opcode_lengths;
			ArrayList include_directories;
			ArrayList source_files;

			long st_address;
			int st_line;
			int st_file;
			int st_column;
			bool is_stmt;
			bool basic_block;
			bool end_sequence;
			bool prologue_end;
			bool epilogue_begin;

			protected enum StandardOpcode
			{
				extended_op		= 0,
				copy			= 1,
				advance_pc		= 2,
				advance_line		= 3,
				set_file		= 4,
				set_column		= 5,
				negate_stmt		= 6,
				set_basic_block		= 7,
				const_add_pc		= 8,
				fixed_advance_pc	= 9,
				set_prologue_end	= 10,
				set_epilogue_begin	= 11,
				set_isa			= 12
			}

			protected enum ExtendedOpcode
			{
				end_sequence		= 1,
				set_address		= 2,
				define_file		= 3
			}

			protected struct FileEntry {
				public readonly string FileName;
				public readonly int Directory;
				public readonly int LastModificationTime;
				public readonly int Length;

				public FileEntry (DwarfBinaryReader reader)
				{
					FileName = reader.ReadString ();
					Directory = reader.ReadLeb128 ();
					LastModificationTime = reader.ReadLeb128 ();
					Length = reader.ReadLeb128 ();
				}
			}

			void initialize ()
			{
				st_address = 0;
				st_file = 0;
				st_line = 1;
				st_column = 0;
				is_stmt = default_is_stmt;
				basic_block = false;
				end_sequence = false;
				prologue_end = false;
				epilogue_begin = false;
			}

			void commit ()
			{
				basic_block = false;
				prologue_end = false;
				epilogue_begin = false;
			}

			void warning (string message)
			{
				Console.WriteLine (message);
			}

			void error (string message)
			{
				throw new DwarfException (dwarf, message);
			}

			void debug (string message, params object[] args)
			{
				// Console.WriteLine (String.Format (message, args));
			}

			void do_standard_opcode (byte opcode)
			{
				debug ("STANDARD OPCODE: {0:x}", opcode);

				switch (opcode) {
				case StandardOpcode.copy:
					commit ();
					break;

				case StandardOpcode.advance_pc:
					st_address += minimum_insn_length * reader.ReadLeb128 ();
					break;

				case StandardOpcode.advance_line:
					st_line += reader.ReadLeb128 ();
					break;

				case StandardOpcode.set_file:
					st_file = reader.ReadLeb128 ();
					break;

				case StandardOpcode.set_column:
					st_column = reader.ReadLeb128 ();
					break;

				case StandardOpcode.const_add_pc:
					break;

				default:
					error (String.Format (
						"Unknown standard opcode {0:x} in line number engine",
						opcode));
					break;
				}
			}

			void do_extended_opcode ()
			{
				byte size = reader.ReadByte ();
				long end_pos = reader.Position + size;
				byte opcode = reader.ReadByte ();

				debug ("EXTENDED OPCODE: {0:x} {1:x}", size, opcode);

				switch (opcode) {
				case ExtendedOpcode.set_address:
					st_address = reader.ReadAddress ();
					break;

				case ExtendedOpcode.end_sequence:
					initialize ();
					break;

				default:
					warning (String.Format (
						"Unknown extended opcode {0:x} in line number engine",
						opcode));
					break;
				}

				reader.Position = end_pos;
			}

			public LineNumberEngine (DwarfReader dwarf, long offset)
			{
				this.dwarf = dwarf;
				this.offset = offset;
				this.reader = dwarf.DebugLineReader;

				reader.Position = offset;
				length = reader.ReadInitialLength ();
				end_offset = reader.Position + length;
				version = reader.ReadInt16 ();
				header_length = reader.ReadOffset ();
				data_offset = reader.Position + header_length;
				minimum_insn_length = reader.ReadByte ();
				default_is_stmt = reader.ReadByte () != 0;
				line_base = reader.ReadByte ();
				line_range = reader.ReadByte ();
				opcode_base = reader.ReadByte ();
				standard_opcode_lengths = new int [opcode_base - 1];
				for (int i = 0; i < opcode_base - 1; i++)
					standard_opcode_lengths [i] = reader.ReadByte ();
				include_directories = new ArrayList ();
				while (reader.PeekByte () != 0)
					include_directories.Add (reader.ReadString ());
				reader.Position++;
				source_files = new ArrayList ();
				while (reader.PeekByte () != 0)
					source_files.Add (new FileEntry (reader));
				reader.Position++;

				Console.WriteLine (this);

				initialize ();

				reader.Position = data_offset;
				while (reader.Position < end_offset) {
					byte opcode = reader.ReadByte ();
					debug ("OPCODE: {0:x}", opcode);

					if (opcode == 0)
						do_extended_opcode ();
					else if (opcode < opcode_base)
						do_standard_opcode (opcode);

				}
			}

			public override string ToString ()
			{
				return String.Format (
					"LineNumberEngine ({0:x},{1:x},{2},{3} - {4},{5},{6})",
					offset, length, version, header_length,
					default_is_stmt, line_base, line_range, opcode_base);
			}
		}

		protected struct AttributeEntry
		{
			DwarfReader dwarf;
			DwarfAttribute attr;
			DwarfForm form;

			public AttributeEntry (DwarfReader dwarf, DwarfAttribute attr, DwarfForm form)
			{
				this.dwarf = dwarf;
				this.attr = attr;
				this.form = form;
			}

			public DwarfAttribute DwarfAttribute {
				get {
					return attr;
				}
			}

			public DwarfForm DwarfForm {
				get {
					return form;
				}
			}

			public Attribute ReadAttribute (long offset)
			{
				return new Attribute (dwarf, offset, attr, form);
			}
		}

		protected class Attribute
		{
			DwarfReader dwarf;
			DwarfAttribute attr;
			DwarfForm form;
			long offset;

			bool has_datasize, has_data;
			int data_size;
			object data;

			public Attribute (DwarfReader dwarf, long offset,
					  DwarfAttribute attr, DwarfForm form)
			{
				this.dwarf = dwarf;
				this.offset = offset;
				this.attr = attr;
				this.form = form;
			}

			public DwarfAttribute DwarfAttribute {
				get {
					return attr;
				}
			}

			public DwarfForm DwarfForm {
				get {
					return form;
				}
			}

			int get_datasize ()
			{
				switch (form) {
				case DwarfForm.ref1:
				case DwarfForm.data1:
				case DwarfForm.flag:
					return 1;

				case DwarfForm.ref2:
				case DwarfForm.data2:
					return 2;

				case DwarfForm.ref4:
				case DwarfForm.data4:
					return 4;

				case DwarfForm.ref8:
				case DwarfForm.data8:
					return 8;

				case DwarfForm.addr:
				case DwarfForm.ref_addr:
					return dwarf.AddressSize;

				case DwarfForm.block1:
					return dwarf.DebugInfoReader.PeekByte (offset) + 1;

				case DwarfForm.block2:
					return dwarf.DebugInfoReader.PeekInt16 (offset) + 2;

				case DwarfForm.block4:
					return dwarf.DebugInfoReader.PeekInt32 (offset) + 4;

				case DwarfForm.block:
				case DwarfForm.ref_udata: {
					int size, size2;
					size2 = dwarf.DebugInfoReader.PeekLeb128 (offset, out size);
					return size + size2;
				}

				case DwarfForm.udata:
				case DwarfForm.sdata: {
					int size;
					dwarf.DebugInfoReader.PeekLeb128 (offset, out size);
					return size;
				}

				case DwarfForm.strp:
					return dwarf.Is64Bit ? 8 : 4;

				case DwarfForm.cstring: {
					string str = dwarf.DebugInfoReader.PeekString (offset);
					return str.Length + 1;
				}

				default:
					throw new DwarfException (dwarf, String.Format (
						"Unknown DW_FORM: 0x{0:x}", (int) form));
				}
			}

			public int DataSize {
				get {
					if (has_datasize)
						return data_size;

					data_size = get_datasize ();
					has_datasize = true;
					return data_size;
				}
			}

			object read_data ()
			{
				DwarfBinaryReader reader = dwarf.DebugInfoReader;

				switch (form) {
				case DwarfForm.ref1:
				case DwarfForm.data1:
				case DwarfForm.flag:
					data_size = 1;
					return (long) reader.PeekByte (offset);

				case DwarfForm.ref2:
				case DwarfForm.data2:
					data_size = 2;
					return (long) reader.PeekInt16 (offset);

				case DwarfForm.ref4:
				case DwarfForm.data4:
					data_size = 4;
					return (long) reader.PeekInt32 (offset);

				case DwarfForm.ref8:
				case DwarfForm.data8:
					data_size = 8;
					return (long) reader.PeekInt64 (offset);

				case DwarfForm.addr:
					data_size = dwarf.AddressSize;
					return (long) reader.PeekAddress (offset);

				case DwarfForm.cstring: {
					string retval = reader.PeekString (offset);
					data_size = retval.Length + 1;
					return retval;
				}

				case DwarfForm.block1:
					data_size = reader.PeekByte (offset);
					return reader.PeekBuffer (offset + 1, data_size);

				case DwarfForm.block2:
					data_size = reader.PeekInt16 (offset);
					return reader.PeekBuffer (offset + 2, data_size);

				case DwarfForm.block4:
					data_size = reader.PeekInt32 (offset);
					return reader.PeekBuffer (offset + 4, data_size);

				case DwarfForm.block: {
					int size;
					data_size = reader.PeekLeb128 (offset, out size);
					return reader.PeekBuffer (offset + size, data_size);
				}

				case DwarfForm.strp: {
					long str_offset = reader.PeekOffset (offset, out data_size);
					return dwarf.DebugStrReader.PeekString (str_offset);
				}

				case DwarfForm.ref_udata:
				case DwarfForm.udata:
				case DwarfForm.sdata:
					return (long) reader.PeekLeb128 (offset, out data_size);

				case DwarfForm.ref_addr:
					return (long) reader.PeekOffset (offset, out data_size);

				default:
					throw new DwarfException (
						dwarf, String.Format ("Unknown DW_FORM: 0x{0:x}", (int) form));
				}
			}

			public object Data {
				get {
					if (has_data)
						return data;

					data = read_data ();
					has_datasize = true;
					has_data = true;
					return data;
				}
			}

			public override string ToString ()
			{
				return String.Format ("Attribute ({2}({0:x}),{3}({1:x}))",
						      (int) attr, (int) form, attr, form);
			}
		}

		protected class AbbrevEntry
		{
			DwarfReader dwarf;
			int abbrev_id;
			DwarfTag tag;
			bool has_children;

			public readonly ArrayList Attributes;

			public AbbrevEntry (DwarfReader dwarf, DwarfBinaryReader reader)
			{
				this.dwarf = dwarf;

				abbrev_id = reader.ReadLeb128 ();
				tag = (DwarfTag) reader.ReadLeb128 ();
				has_children = reader.ReadByte () != 0;

				Attributes = new ArrayList ();

				do {
					int attr = reader.ReadLeb128 ();
					int form = reader.ReadLeb128 ();

					if ((attr == 0) && (form == 0))
						break;

					Attributes.Add (new AttributeEntry (
						dwarf, (DwarfAttribute) attr, (DwarfForm) form));
				} while (true);
			}

			public int ID {
				get {
					return abbrev_id;
				}
			}

			public DwarfTag Tag {
				get {
					return tag;
				}
			}

			public bool HasChildren {
				get {
					return has_children;
				}
			}

			public override string ToString ()
			{
				return String.Format ("AbbrevEntry ({0},{1},{2})",
						      abbrev_id, tag, has_children);
			}
		}

		protected class Die
		{
			public readonly CompilationUnit comp_unit;
			public readonly DwarfReader dwarf;
			public readonly AbbrevEntry abbrev;
			public readonly long Offset;
			public readonly long ChildrenOffset;

			protected virtual int ReadAttributes ()
			{
				int total_size = 0;

				foreach (AttributeEntry entry in abbrev.Attributes) {
					Attribute attribute = entry.ReadAttribute (Offset + total_size);
					total_size += attribute.DataSize;
				}

				return total_size;
			}

			WeakReference weak_children;

			protected virtual ArrayList ReadChildren (DwarfBinaryReader reader)
			{
				if (!abbrev.HasChildren)
					return null;

				ArrayList children = new ArrayList ();

				while (reader.PeekByte () != 0)
					children.Add (Die.CreateDie (reader, comp_unit));

				reader.Position++;
				return children;
			}

			public ArrayList Children {
				get {
					if (!abbrev.HasChildren)
						return null;

					ArrayList children = null;
					if (weak_children != null) {
						try {
							children = (ArrayList) weak_children.Target;
						} catch {
							weak_children = null;
						}
					}

					if (children != null)
						return children;

					DwarfBinaryReader reader = dwarf.DebugInfoReader;

					long old_pos = reader.Position;
					reader.Position = ChildrenOffset;
					children = ReadChildren (reader);
					reader.Position = old_pos;
					weak_children = new WeakReference (children);

					return children;
				}
			}

			protected Die (DwarfBinaryReader reader, CompilationUnit comp_unit, AbbrevEntry abbrev)
			{
				this.comp_unit = comp_unit;
				this.dwarf = comp_unit.DwarfReader;
				this.abbrev = abbrev;

				Offset = reader.Position;
				ChildrenOffset = Offset + ReadAttributes ();
				reader.Position = ChildrenOffset;

				if (this is DieCompileUnit)
					return;

				ReadChildren (reader);
			}

			protected static AbbrevEntry get_abbrev (DwarfBinaryReader reader,
								 CompilationUnit comp_unit)
			{
				int abbrev_id = reader.ReadLeb128 ();
				return comp_unit [abbrev_id];
			}

			protected static AbbrevEntry get_abbrev (DwarfBinaryReader reader,
								 CompilationUnit comp_unit, DwarfTag tag)
			{
				AbbrevEntry abbrev = get_abbrev (reader, comp_unit);

				if (abbrev.Tag == tag)
					return abbrev;

				throw new DwarfException (
					comp_unit.DwarfReader, String.Format (
						"Expected tag {0}, but found {1}", tag, abbrev.Tag));
			}

			public static Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit)
			{
				AbbrevEntry abbrev = get_abbrev (reader, comp_unit);

				switch (abbrev.Tag) {
				case DwarfTag.compile_unit:
					return new DieCompileUnit (reader, comp_unit, abbrev);

				case DwarfTag.subprogram:
					return new DieSubprogram (reader, comp_unit, abbrev);

				default:
					return new Die (reader, comp_unit, abbrev);
				}
			}
		}

		protected class DieCompileUnit : Die, ISymbolContainer
		{
			public DieCompileUnit (DwarfBinaryReader reader, CompilationUnit comp_unit,
					       AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			public DieCompileUnit (DwarfBinaryReader reader, CompilationUnit comp_unit)
				: this (reader, comp_unit,
					get_abbrev (reader, comp_unit, DwarfTag.compile_unit))
			{ }

			long start_pc, end_pc;
			string name;
			bool is_continuous;

			protected long line_offset;
			protected bool has_lines;

			LineNumberEngine line_numbers;

			internal void ReadLineNumbers ()
			{
				if (!has_lines || (line_numbers != null))
					return;

				line_numbers = new LineNumberEngine (dwarf, line_offset);
			}

			protected override int ReadAttributes ()
			{
				int total_size = 0;
				foreach (AttributeEntry entry in abbrev.Attributes) {
					Attribute attribute = entry.ReadAttribute (Offset + total_size);

					switch (attribute.DwarfAttribute) {
					case DwarfAttribute.low_pc:
						start_pc = (long) attribute.Data;
						break;

					case DwarfAttribute.high_pc:
						end_pc = (long) attribute.Data;
						break;

					case DwarfAttribute.stmt_list:
						line_offset = (long) attribute.Data;
						has_lines = true;
						break;

					default:
						break;
					}

					total_size += attribute.DataSize;
				}

				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;

				return total_size;
			}

			public string ImageFile {
				get {
					return dwarf.FileName;
				}
			}

			public bool IsContinuous {
				get {
					return is_continuous;
				}
			}

			public ITargetLocation StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return new TargetLocation (start_pc);
				}
			}

			public ITargetLocation EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return new TargetLocation (end_pc);
				}
			}
		}

		protected class DieSubprogram : Die, ISymbolContainer
		{
			long start_pc, end_pc;
			bool is_continuous;
			string name;

			protected override int ReadAttributes ()
			{
				int total_size = 0;
				foreach (AttributeEntry entry in abbrev.Attributes) {
					Attribute attribute = entry.ReadAttribute (Offset + total_size);

					switch (attribute.DwarfAttribute) {
					case DwarfAttribute.low_pc:
						start_pc = (long) attribute.Data;
						break;

					case DwarfAttribute.high_pc:
						end_pc = (long) attribute.Data;
						break;

					case DwarfAttribute.name:
						name = (string) attribute.Data;
						break;

					default:
						break;
					}

					total_size += attribute.DataSize;
				}

				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;

				return total_size;
			}

			public DieSubprogram (DwarfBinaryReader reader, CompilationUnit comp_unit,
						AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			public string Name {
				get {
					return name != null ? name : "<unknown>";
				}
			}

			public bool IsContinuous {
				get {
					return is_continuous;
				}
			}

			public ITargetLocation StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return new TargetLocation (start_pc);
				}
			}

			public ITargetLocation EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return new TargetLocation (end_pc);
				}
			}
		}

		protected class CompilationUnit
		{
			DwarfReader dwarf;
			long start_offset, unit_length, abbrev_offset;
			int version, address_size;
			DieCompileUnit comp_unit_die;
			DwarfSymbolTable symtab;
			Hashtable abbrevs;

			public CompilationUnit (DwarfReader dwarf, DwarfBinaryReader reader)
			{
				this.dwarf = dwarf;

				unit_length = reader.ReadInitialLength ();
				start_offset = reader.Position;
				version = reader.ReadInt16 ();
				abbrev_offset = reader.ReadOffset ();
				address_size = reader.ReadByte ();

				if (version < 2)
					throw new DwarfException (
						dwarf, String.Format ("Wrong DWARF version: {0}", version));

				Console.WriteLine (this);

				abbrevs = new Hashtable ();

				DwarfBinaryReader abbrev_reader = dwarf.DebugAbbrevReader;

				abbrev_reader.Position = abbrev_offset;
				while (abbrev_reader.PeekByte () != 0) {
					AbbrevEntry entry = new AbbrevEntry (dwarf, abbrev_reader);
					abbrevs.Add (entry.ID, entry);
				}

				comp_unit_die = new DieCompileUnit (reader, this);

				symtab = new DwarfSymbolTable (comp_unit_die);

				reader.Position = start_offset + unit_length;
			}

			public DwarfReader DwarfReader {
				get {
					return dwarf;
				}
			}

			public DieCompileUnit DieCompileUnit {
				get {
					return comp_unit_die;
				}
			}

			public ISymbolTable SymbolTable {
				get {
					return symtab;
				}
			}

			public AbbrevEntry this [int abbrev_id] {
				get {
					if (abbrevs.Contains (abbrev_id))
						return (AbbrevEntry) abbrevs [abbrev_id];

					throw new DwarfException (dwarf, String.Format (
						"{0} does not contain an abbreviation entry {1}",
						this, abbrev_id));
				}
			}

			public override string ToString ()
			{
				return String.Format ("CompilationUnit ({0},{1},{2} - {3},{4},{5})",
						      dwarf.Is64Bit ? "64-bit" : "32-bit", version,
						      address_size, start_offset, unit_length,
						      abbrev_offset);
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
					bfd.Dispose ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DwarfReader ()
		{
			Dispose (false);
		}
	}
}
