using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages.Native;

namespace Mono.Debugger.Architecture
{
	internal class DwarfReader
	{
		protected Bfd bfd;
		protected Module module;
		protected string filename;
		bool is64bit;
		byte address_size;

		ObjectCache debug_info_reader;
		ObjectCache debug_abbrev_reader;
		ObjectCache debug_line_reader;
		ObjectCache debug_aranges_reader;
		ObjectCache debug_pubnames_reader;
		ObjectCache debug_str_reader;

		Hashtable compile_unit_hash;
		DwarfSymbolTable symtab;
		ArrayList aranges;
		TargetInfo target_info;
		SourceFileFactory factory;

		SourceFile[] sources;

		protected class DwarfException : Exception
		{
			public DwarfException (DwarfReader reader, string message)
				: base (String.Format ("{0}: {1}", reader.FileName, message))
			{ }

			public DwarfException (DwarfReader reader, string message, Exception inner)
				: base (String.Format ("{0}: {1}", reader.FileName, message), inner)
			{ }
		}

		public DwarfReader (Bfd bfd, Module module, SourceFileFactory factory)
		{
			this.bfd = bfd;
			this.module = module;
			this.filename = bfd.FileName;
			this.factory = factory;

			debug_info_reader = create_reader (".debug_info");

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

			target_info = reader.TargetInfo = new TargetInfo (address_size);

			debug_abbrev_reader = create_reader (".debug_abbrev");
			debug_line_reader = create_reader (".debug_line");
			debug_aranges_reader = create_reader (".debug_aranges");
			debug_pubnames_reader = create_reader (".debug_pubnames");
			debug_str_reader = create_reader (".debug_str");

			compile_unit_hash = Hashtable.Synchronized (new Hashtable ());

			aranges = ArrayList.Synchronized (read_aranges ());

			symtab = new DwarfSymbolTable (this, aranges);

			ArrayList source_list = new ArrayList ();

			long offset = 0;
			while (offset < reader.Size) {
				CompileUnitBlock block = new CompileUnitBlock (this, offset);
				compile_unit_hash.Add (offset, block);
				offset += block.length;

				source_list.AddRange (block.Sources);
			}

			sources = new SourceFile [source_list.Count];
			source_list.CopyTo (sources, 0);
		}

		public TargetInfo TargetInfo {
			get {
				return target_info;
			}
		}

		protected TargetAddress GetAddress (long address)
		{
			return bfd.GetAddress (address);
		}

		protected ISymbolTable get_symtab_at_offset (long offset)
		{
			CompileUnitBlock block = (CompileUnitBlock) compile_unit_hash [offset];

			// This either return the already-read symbol table or acquire the
			// thread lock and read it.
			return block.SymbolTable;
		}

		public SourceFile[] Sources {
			get {
				return sources;
			}
		}

		protected class CompileUnitBlock
		{
			public readonly DwarfReader dwarf;
			public readonly long offset;
			public readonly long length;

			SymbolTableCollection symtabs;
			ArrayList compile_units;
			SourceFile[] sources;
			bool initialized;

			public IMethod Lookup (TargetAddress address)
			{
				build_symtabs ();
				return symtabs.Lookup (address);
			}

			public ISymbolTable SymbolTable {
				get {
					build_symtabs ();
					return symtabs;
				}
			}

			public SourceFile[] Sources {
				get {
					return sources;
				}
			}

			void build_symtabs ()
			{
				// If we're already initialized, we don't need to do any locking,
				// so do this check here without locking.
				if (initialized)
					return;

				lock (this) {
					// We need to check this again after we acquired the thread
					// lock to avoid a race condition.
					if (initialized)
						return;

					symtabs = new SymbolTableCollection ();
					symtabs.Lock ();

					foreach (CompilationUnit comp_unit in compile_units)
						symtabs.AddSymbolTable (comp_unit.SymbolTable);

					symtabs.UnLock ();

					initialized = true;
				}
			}

			public CompileUnitBlock (DwarfReader dwarf, long start)
			{
				this.dwarf = dwarf;
				this.offset = start;

				DwarfBinaryReader reader = dwarf.DebugInfoReader;
				reader.Position = offset;
				long length_field = reader.ReadInitialLength ();
				long stop = reader.Position + length_field;
				length = stop - offset;
				int version = reader.ReadInt16 ();

				if (version < 2)
					throw new DwarfException (dwarf, String.Format (
						"Wrong DWARF version: {0}", version));

				reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				reader.Position = offset;

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (dwarf, String.Format (
						"Unknown address size: {0}", address_size));

				ArrayList source_list = new ArrayList ();
				compile_units = new ArrayList ();

				while (reader.Position < stop) {
					CompilationUnit comp_unit = new CompilationUnit (dwarf, reader);
					compile_units.Add (comp_unit);
					source_list.Add (comp_unit.DieCompileUnit.SourceFile);
				}

				sources = new SourceFile [source_list.Count];
				source_list.CopyTo (sources, 0);
			}
		}

		public ISymbolTable SymbolTable {
			get {
				return symtab;
			}
		}

		protected class DwarfSymbolTable : SymbolTable
		{
			DwarfReader dwarf;
			ArrayList ranges;

			public DwarfSymbolTable (DwarfReader dwarf, ArrayList ranges)
			{
				this.dwarf = dwarf;
				this.ranges = ranges;
				this.ranges.Sort ();
			}

			public override bool HasRanges {
				get {
					return true;
				}
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ISymbolRange[] retval = new ISymbolRange [ranges.Count];
					ranges.CopyTo (retval, 0);
					return retval;
				}
			}

			public override bool HasMethods {
				get {
					return false;
				}
			}

			protected override ArrayList GetMethods ()
			{
				throw new InvalidOperationException ();
			}

			public ArrayList GetAllMethods ()
			{
				ArrayList methods = new ArrayList ();

				foreach (RangeEntry range in ranges) {
					ISymbolTable symtab = dwarf.get_symtab_at_offset (range.FileOffset);

					if (!symtab.IsLoaded || !symtab.HasMethods)
						continue;

					methods.AddRange (symtab.Methods);
				}

				return methods;
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2})", GetType (), dwarf.FileName,
						      ranges.Count);
			}
		}

		private class RangeEntry : SymbolRangeEntry
		{
			public readonly long FileOffset;

			DwarfReader dwarf;

			public RangeEntry (DwarfReader dwarf, long offset,
					   TargetAddress address, long size)
				: base (address, address + size)
			{
				this.dwarf = dwarf;
				this.FileOffset = offset;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return dwarf.get_symtab_at_offset (FileOffset);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry ({0}:{1}:{2})",
						      StartAddress, EndAddress, FileOffset);
			}
		}

		ArrayList read_aranges ()
		{
			DwarfBinaryReader reader = DebugArangesReader;

			ArrayList ranges = new ArrayList ();

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

					TargetAddress taddress = GetAddress (address);
					ranges.Add (new RangeEntry (this, offset, taddress, size));
				}
			}

			return ranges;
		}

		object create_reader_func (object user_data)
		{
			byte[] contents = bfd.GetSectionContents ((string) user_data, false);
			if (contents == null)
				throw new DwarfException (this, "Can't find DWARF 2 debugging info");

			ITargetInfo target_info = null;
			if (AddressSize != 0)
				target_info = new TargetInfo (AddressSize);

			return new TargetBlob (contents);
		}

		ObjectCache create_reader (string section_name)
		{
			return new ObjectCache (new ObjectCacheFunc (create_reader_func), section_name, 5);
		}

		//
		// These properties always create a new DwarfBinaryReader instance, but all these instances
		// share the buffer they're reading from.  A DwarfBinaryReader just contains a reference to
		// the data and the current position - so by creating a new instance each time we start a
		// read operation, reading will be thread-safe.
		//

		public DwarfBinaryReader DebugInfoReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_info_reader.Data);
			}
		}

		public DwarfBinaryReader DebugAbbrevReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_abbrev_reader.Data);
			}
		}

		public DwarfBinaryReader DebugPubnamesReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_pubnames_reader.Data);
			}
		}

		public DwarfBinaryReader DebugLineReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_line_reader.Data);
			}
		}

		public DwarfBinaryReader DebugArangesReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_aranges_reader.Data);
			}
		}

		public DwarfBinaryReader DebugStrReader {
			get {
				return new DwarfBinaryReader (this, (TargetBlob) debug_str_reader.Data);
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

		public class DwarfBinaryReader : TargetBinaryReader
		{
			DwarfReader dwarf;
			bool is64bit;

			public DwarfBinaryReader (DwarfReader dwarf, TargetBlob blob)
				: base (blob, dwarf.TargetInfo)
			{
				this.dwarf = dwarf;
				this.is64bit = dwarf.Is64Bit;
			}

			public DwarfReader DwarfReader {
				get {
					return dwarf;
				}
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
			subroutine_type		= 0x15,
			typedef			= 0x16,
			comp_dir		= 0x1b,
			inheritance		= 0x1c,
			subrange_type		= 0x21,
			access_declaration	= 0x23,
			base_type		= 0x24,
			const_type		= 0x26,
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
			prototyped		= 0x27,
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

		public enum DwarfBaseTypeEncoding {
			address			= 0x01,
			boolean			= 0x02,
			complex_float		= 0x03,
			normal_float		= 0x04,
			signed			= 0x05,
			signed_char		= 0x06,
			unsigned		= 0x07,
			unsigned_char		= 0x08,
			imaginary_float		= 0x09
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
			protected DwarfReader dwarf;
			protected DwarfBinaryReader reader;
			protected byte minimum_insn_length;
			protected bool default_is_stmt;
			protected byte opcode_base;
			protected int line_base, line_range;
			protected ArrayList source_files;

			long offset;

			long length;
			int version;
			long header_length, data_offset, end_offset;
			long base_address;

			ISymbolContainer next_method, current_method;
			TargetAddress next_method_address;
			int next_method_index;

			int[] standard_opcode_lengths;
			ArrayList include_directories;
			string compilation_dir;
			ArrayList methods;
			Hashtable method_hash;

			protected class StatementMachine
			{
				public LineNumberEngine engine;			      
				public TargetAddress st_address;
				public int st_line;
				public int st_file;
				public int st_column;
				public bool is_stmt;
				public bool basic_block;
				public bool end_sequence;
				public bool prologue_end;
				public bool epilogue_begin;
				public long start_offset;

				public int start_file;
				public int start_line, end_line;
				public ArrayList lines;

				bool creating;
				DwarfBinaryReader reader;
				int const_add_pc_range;

				public StatementMachine (LineNumberEngine engine, long offset)
				{
					this.engine = engine;
					this.reader = this.engine.reader;
					this.creating = true;
					this.start_offset = offset;
					this.st_address = TargetAddress.Null;
					this.st_file = 1;
					this.st_line = 1;
					this.st_column = 0;
					this.is_stmt = this.engine.default_is_stmt;
					this.basic_block = false;
					this.end_sequence = false;
					this.prologue_end = false;
					this.epilogue_begin = false;
					this.start_file = st_file;

					this.const_add_pc_range =
						((0xff - engine.opcode_base) / engine.line_range) *
						engine.minimum_insn_length;
					this.lines = new ArrayList ();
				}

				public StatementMachine (StatementMachine stm)
				{
					this.engine = stm.engine;
					this.reader = this.engine.reader;
					this.creating = false;
					this.start_offset = stm.start_offset;
					this.st_address = stm.st_address;
					this.st_file = stm.st_file;
					this.st_line = stm.st_line;
					this.st_column = stm.st_column;
					this.is_stmt = this.engine.default_is_stmt;
					this.basic_block = stm.basic_block;
					this.end_sequence = stm.end_sequence;
					this.prologue_end = stm.prologue_end;
					this.epilogue_begin = stm.epilogue_begin;
					this.start_file = stm.st_file;

					engine.debug ("CLONE: {0} {1} {2} - {3}",
						      stm.start_line, stm.end_line, stm.st_file,
						      engine.source_files [stm.st_file]);

					this.start_line = stm.start_line;
					this.end_line = stm.end_line;

					this.const_add_pc_range =
						((0xff - engine.opcode_base) / engine.line_range) *
						engine.minimum_insn_length;

					this.lines = stm.lines;
					this.lines.Sort ();

					stm.start_line = stm.st_line;
					stm.lines = new ArrayList ();
				}

				void commit ()
				{
					if (creating) {
						if (start_line == 0)
							start_line = st_line;
						engine.commit (this);
						end_line = st_line;
					}

					if (st_file == start_file)
						lines.Add (new LineEntry (st_address, st_line));

					basic_block = false;
					prologue_end = false;
					epilogue_begin = false;
				}

				void set_end_sequence ()
				{
					engine.debug ("SET END SEQUENCE");

					end_sequence = true;

					if (!creating)
						return;

					engine.end_sequence (this);

					end_line = st_line;
				}

				void do_standard_opcode (byte opcode)
				{
					engine.debug ("STANDARD OPCODE: {0:x}", opcode);

					switch ((StandardOpcode) opcode) {
					case StandardOpcode.copy:
						commit ();
						break;

					case StandardOpcode.advance_pc:
						st_address += engine.minimum_insn_length * reader.ReadLeb128 ();
						break;

					case StandardOpcode.advance_line:
						st_line += reader.ReadSLeb128 ();
						break;

					case StandardOpcode.set_file:
						st_file = reader.ReadLeb128 ();
						engine.debug ("FILE: {0}", st_file);
						break;

					case StandardOpcode.set_column:
						st_column = reader.ReadLeb128 ();
						break;

					case StandardOpcode.const_add_pc:
						st_address += const_add_pc_range;
						break;

					case StandardOpcode.set_prologue_end:
						prologue_end = true;
						break;

					case StandardOpcode.set_epilogue_begin:
						epilogue_begin = true;
						break;

					default:
						engine.error (String.Format (
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

					engine.debug ("EXTENDED OPCODE: {0:x} {1:x}", size, opcode);

					switch ((ExtendedOpcode) opcode) {
					case ExtendedOpcode.set_address:
						st_address = engine.dwarf.GetAddress (reader.ReadAddress ());
						engine.debug ("SETTING ADDRESS TO {0:x}", st_address);
						break;

					case ExtendedOpcode.end_sequence:
						set_end_sequence ();
						break;

					default:
						engine.warning (String.Format (
							"Unknown extended opcode {0:x} in line number " +
							"engine at offset {1}", opcode, reader.Position));
						break;
					}

					reader.Position = end_pos;
				}

				void dump ()
				{
					FileEntry file = (FileEntry) engine.source_files [st_file];
					Console.WriteLine ("DUMP: {0} {1} {2:x} - {3:x}",
							   file, st_line, st_address,
							   engine.next_method_address);
				}

				public void Read ()
				{
					reader.Position = start_offset;
					end_sequence = false;

					while (!end_sequence) {
						byte opcode = reader.ReadByte ();
						engine.debug ("OPCODE: {0:x}", opcode);

						if (opcode == 0)
							do_extended_opcode ();
						else if (opcode < engine.opcode_base)
							do_standard_opcode (opcode);
						else {
							opcode -= engine.opcode_base;

							int addr_inc = (opcode / engine.line_range) *
								engine.minimum_insn_length;
							int line_inc = engine.line_base +
								(opcode % engine.line_range);

							engine.debug (
								"INC: {0:x} {1:x} {2:x} {3:x} - {4} {5}",
								opcode, engine.opcode_base, addr_inc, line_inc,
								opcode % engine.line_range,
								opcode / engine.line_range);

							st_line += line_inc;
							st_address += addr_inc;

							commit ();
						}
					}
				}
			}

			// StatementMachine stm;

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

				public override string ToString ()
				{
					return String.Format ("FileEntry({0},{1})", FileName, Directory);
				}
			}

			void end_sequence (StatementMachine stm)
			{
				debug ("NEXT: {0:x} {1:x} {2} {3} {4}", stm.st_address, next_method_address,
				       methods.Count, current_method, next_method);

				if (current_method != null)
					method_hash.Add (current_method, new StatementMachine (stm));
				current_method = next_method;
				if (next_method_index < methods.Count) {
					next_method = (ISymbolContainer) methods [next_method_index++];
					next_method_address = next_method.StartAddress;
				} else
					next_method_address = TargetAddress.Null;

				debug ("NEW NEXT: {0:x}", next_method_address);
			}

			void commit (StatementMachine stm)
			{
				debug ("COMMIT: {0:x} {1} {2:x}", stm.st_address, stm.st_line,
				       next_method_address);

				if (!next_method_address.IsNull && (stm.st_address >= next_method_address))
					end_sequence (stm);
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

			public LineNumberEngine (DwarfReader dwarf, long offset, string compilation_dir,
						 ArrayList methods)
			{
				this.dwarf = dwarf;
				this.offset = offset;
				this.reader = dwarf.DebugLineReader;
				this.methods = methods;
				this.compilation_dir = compilation_dir;

				base_address = dwarf.bfd.BaseAddress.Address;

				reader.Position = offset;
				length = reader.ReadInitialLength ();
				end_offset = reader.Position + length;
				version = reader.ReadInt16 ();
				header_length = reader.ReadOffset ();
				data_offset = reader.Position + header_length;
				minimum_insn_length = reader.ReadByte ();
				default_is_stmt = reader.ReadByte () != 0;
				line_base = (sbyte) reader.ReadByte ();
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

				next_method_index = 1;
				if (methods.Count > 0) {
					next_method = (ISymbolContainer) methods [0];
					next_method_address = next_method.StartAddress;
				}

				method_hash = new Hashtable ();

				StatementMachine stm = new StatementMachine (this, reader.Position);
				stm.Read ();

				if ((current_method != null) && !method_hash.Contains (current_method))
					method_hash.Add (current_method, new StatementMachine (stm));
			}

			public string GetSource (ISymbolContainer method, out int start_row, out int end_row,
						 out ArrayList addresses)
			{
				start_row = end_row = 0;
				addresses = null;

				StatementMachine stm = (StatementMachine) method_hash [method];
				if (stm == null)
					return null;

				FileEntry file = (FileEntry) source_files [stm.st_file];
				start_row = stm.start_line;
				end_row = stm.end_line;
				addresses = stm.lines;

				string dir_name;
				if (file.Directory > 0)
					dir_name = (string) include_directories [file.Directory - 1];
				else
					dir_name = compilation_dir;

				if (dir_name != null)
					return String.Format (
						"{0}{1}{2}", dir_name, Path.DirectorySeparatorChar,
						file.FileName);
				else
					return file.FileName;
			}

			public override string ToString ()
			{
				return String.Format (
					"LineNumberEngine ({0:x},{1:x},{2},{3} - {4},{5},{6},{7})",
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
				case DwarfForm.flag:
					data_size = 1;
					return reader.PeekByte (offset) != 0;

				case DwarfForm.ref1:
				case DwarfForm.data1:
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
					data_size = reader.PeekByte (offset) + 1;
					return reader.PeekBuffer (offset + 1, data_size - 1);

				case DwarfForm.block2:
					data_size = reader.PeekInt16 (offset) + 2;
					return reader.PeekBuffer (offset + 2, data_size - 2);

				case DwarfForm.block4:
					data_size = reader.PeekInt32 (offset) + 4;
					return reader.PeekBuffer (offset + 4, data_size - 4);

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

			protected virtual int ReadAttributes (DwarfBinaryReader reader)
			{
				int total_size = 0;

				foreach (AttributeEntry entry in abbrev.Attributes) {
					Attribute attribute = entry.ReadAttribute (Offset + total_size);
					ProcessAttribute (attribute);
					total_size += attribute.DataSize;
				}

				return total_size;
			}

			protected virtual void ProcessAttribute (Attribute attribute)
			{ }

			ArrayList children;

			protected virtual ArrayList ReadChildren (DwarfBinaryReader reader)
			{
				if (!abbrev.HasChildren)
					return null;

				children = new ArrayList ();

				while (reader.PeekByte () != 0)
					children.Add (CreateDie (reader, comp_unit));

				reader.Position++;
				return children;
			}

			public ArrayList Children {
				get {
					if (!abbrev.HasChildren)
						return null;

					if (children == null) {
						DwarfBinaryReader reader = dwarf.DebugInfoReader;

						long old_pos = reader.Position;
						reader.Position = ChildrenOffset;
						children = ReadChildren (reader);
						reader.Position = old_pos;
					}

					return children;
				}
			}

			protected Die (DwarfBinaryReader reader, CompilationUnit comp_unit,
				       AbbrevEntry abbrev)
			{
				this.comp_unit = comp_unit;
				this.dwarf = comp_unit.DwarfReader;
				this.abbrev = abbrev;

				Offset = reader.Position;
				ChildrenOffset = Offset + ReadAttributes (reader);
				reader.Position = ChildrenOffset;

				if (this is DieCompileUnit)
					return;

				ReadChildren (reader);
			}

			public Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit)
			{
				long offset = reader.Position;
				int abbrev_id = reader.ReadLeb128 ();
				AbbrevEntry abbrev = comp_unit [abbrev_id];

				return CreateDie (reader, comp_unit, offset, abbrev);
			}

			public static DieCompileUnit CreateDieCompileUnit (DwarfBinaryReader reader,
									   CompilationUnit comp_unit)
			{
				long offset = reader.Position;
				int abbrev_id = reader.ReadLeb128 ();
				AbbrevEntry abbrev = comp_unit [abbrev_id];

				return new DieCompileUnit (reader, comp_unit, abbrev);
			}

			protected virtual Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							 long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.compile_unit:
					throw new InternalError ();

				case DwarfTag.subprogram:
					return new DieSubprogram (reader, comp_unit, abbrev);

				case DwarfTag.base_type:
					return new DieBaseType (reader, comp_unit, offset, abbrev);

				case DwarfTag.const_type:
					return new DieConstType (reader, comp_unit, offset, abbrev);

				case DwarfTag.pointer_type:
					return new DiePointerType (reader, comp_unit, offset, abbrev);

				case DwarfTag.structure_type:
					return new DieStructureType (reader, comp_unit, offset, abbrev);

				case DwarfTag.typedef:
					return new DieTypedef (reader, comp_unit, offset, abbrev);

				case DwarfTag.subroutine_type:
					return new DieSubroutineType (reader, comp_unit, offset, abbrev);

				case DwarfTag.member:
					return new DieMember (reader, comp_unit, abbrev);

				default:
					return new Die (reader, comp_unit, abbrev);
				}
			}

			public DieCompileUnit DieCompileUnit {
				get { return comp_unit.DieCompileUnit; }
			}
		}

		protected class DieCompileUnit : Die, ISymbolContainer
		{
			public DieCompileUnit (DwarfBinaryReader reader, CompilationUnit comp_unit,
					       AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;

				string file_name;
				if (comp_dir != null)
					file_name = String.Concat (
						comp_dir, Path.DirectorySeparatorChar, name);
				else
					file_name = name;
				file = new DwarfSourceFile (dwarf, file_name);
				symtab = new CompileUnitSymbolTable (this);
			}

			long start_pc, end_pc;
			string name;
			string comp_dir;
			bool is_continuous;
			DwarfSourceFile file;
			CompileUnitSymbolTable symtab;
			ArrayList children;
			LineNumberEngine engine;

			protected long line_offset;
			protected bool has_lines;

			void read_children ()
			{
				if (children != null)
					return;

				children = new ArrayList ();

				if (abbrev.HasChildren) {
					foreach (Die child in Children) {
						DieSubprogram subprog = child as DieSubprogram;
						if ((subprog == null) || !subprog.IsContinuous)
							continue;

						children.Add (subprog);
					}

					children.Sort ();
				}

				if (has_lines) {
					engine = new LineNumberEngine (dwarf, line_offset, comp_dir, children);

					foreach (DieSubprogram subprog in children)
						subprog.SetEngine (engine);
				}
			}

			protected LineNumberEngine Engine {
				get {
					read_children ();
					return engine;
				}
			}

			protected ArrayList Subprograms {
				get {
					read_children ();
					return children;
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
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

				case DwarfAttribute.comp_dir:
					comp_dir = (string) attribute.Data;
					break;

				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				}
			}

			public string ImageFile {
				get {
					return dwarf.FileName;
				}
			}

			public string CompilationDirectory {
				get {
					return comp_dir;
				}
			}

			public bool IsContinuous {
				get {
					return is_continuous;
				}
			}

			public long LineNumberOffset {
				get {
					if (!has_lines)
						return -1;

					return line_offset;
				}
			}

			public ISymbolTable SymbolTable {
				get {
					return symtab;
				}
			}

			public TargetAddress StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress (start_pc);
				}
			}

			public TargetAddress EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress (end_pc);
				}
			}

			public SourceFile SourceFile {
				get {
					return file;
				}
			}

			public void AddMethod (SourceMethod method)
			{
				file.AddMethod (method);
			}

			protected class DwarfSourceFile : SourceFile
			{
				DwarfReader dwarf;
				ArrayList methods;

				public DwarfSourceFile (DwarfReader dwarf, string filename)
					: base (dwarf.module, filename)
				{
					this.dwarf = dwarf;
					this.methods = new ArrayList ();
				}

				public void AddMethod (SourceMethod method)
				{
					methods.Add (method);
				}

				protected override ArrayList GetMethods ()
				{
					return methods;
				}
			}

			protected class CompileUnitSymbolTable : SymbolTable
			{
				DieCompileUnit comp_unit_die;

				public CompileUnitSymbolTable (DieCompileUnit comp_unit_die)
					: base (comp_unit_die)
				{
					this.comp_unit_die = comp_unit_die;
				}

				public override bool HasRanges {
					get {
						return false;
					}
				}

				public override ISymbolRange[] SymbolRanges {
					get {
						throw new InvalidOperationException ();
					}
				}

				public override bool HasMethods {
					get {
						return true;
					}
				}

				protected override ArrayList GetMethods ()
				{
					ArrayList methods = new ArrayList ();

					ArrayList list = comp_unit_die.Subprograms;
					LineNumberEngine engine = comp_unit_die.Engine;

					foreach (DieSubprogram subprog in list)
						methods.Add (subprog.Method);

					return methods;
				}
			}
		}

		protected class DieSubprogram : Die, IComparable, ISymbolContainer
		{
			long start_pc, end_pc;
			bool is_continuous;
			string name;
			DwarfTargetMethod method;
			LineNumberEngine engine;
			ArrayList param_dies, local_dies;
			IVariable[] parameters, locals;

			protected override void ProcessAttribute (Attribute attribute)
			{
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
				}
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.formal_parameter:
					return new DieFormalParameter (this, reader, comp_unit, abbrev);

				case DwarfTag.variable:
					return new DieVariable (this, reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			public DieSubprogram (DwarfBinaryReader reader, CompilationUnit comp_unit,
					      AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;
			}

			public SourceFile SourceFile {
				get {
					return DieCompileUnit.SourceFile;
				}
			}

			public string ImageFile {
				get {
					return dwarf.filename;
				}
			}

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

			public TargetAddress StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress (start_pc);
				}
			}

			public TargetAddress EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress (end_pc);
				}
			}

			public int CompareTo (object obj)
			{
				DieSubprogram die = (DieSubprogram) obj;

				if (die.start_pc < start_pc)
					return 1;
				else if (die.start_pc > start_pc)
					return -1;
				else
					return 0;
			}

			public IMethod Method {
				get {
					if (method == null)
						throw new InvalidOperationException ();
					return method;
				}
			}

			public LineNumberEngine Engine {
				get {
					if (engine == null)
						throw new InvalidOperationException ();
					return engine;
				}
			}

			public void SetEngine (LineNumberEngine engine)
			{
				this.engine = engine;
				method = new DwarfTargetMethod (this, engine);
			}

			public void AddParameter (DieMethodVariable variable)
			{
				if (param_dies == null)
					param_dies = new ArrayList ();

				param_dies.Add (variable);
			}

			public void AddLocal (DieMethodVariable variable)
			{
				if (local_dies == null)
					local_dies = new ArrayList ();

				local_dies.Add (variable);
			}

			IVariable[] resolve_variables (ArrayList variables)
			{
				if (variables == null)
					return new IVariable [0];

				ArrayList list = new ArrayList ();
				foreach (DieMethodVariable variable in variables) {
					IVariable resolved = variable.Variable;
					if (resolved != null)
						list.Add (resolved);
				}

				IVariable[] retval = new IVariable [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}

			public IVariable[] Parameters {
				get {
					if (parameters == null)
						parameters = resolve_variables (param_dies);

					return parameters;
				}
			}

			public IVariable[] Locals {
				get {
					if (locals == null)
						locals = resolve_variables (local_dies);

					return locals;
				}
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2:x}:{3:x})", GetType (),
						      Name, start_pc, end_pc);
			}

			protected class DwarfTargetMethod : MethodBase
			{
				LineNumberEngine engine;
				DieSubprogram subprog;
				DwarfSourceMethod source;
				int start_row, end_row;
				ArrayList addresses;
				ISourceBuffer buffer;

				public DwarfTargetMethod (DieSubprogram subprog, LineNumberEngine engine)
					: base (subprog.Name, subprog.ImageFile, subprog.dwarf.module)
				{
					this.subprog = subprog;
					this.engine = engine;

					if (subprog.IsContinuous)
						SetAddresses (subprog.StartAddress, subprog.EndAddress);

					read_source ();
					SetSource (new DwarfTargetMethodSource (this, subprog.SourceFile));
				}

				public override object MethodHandle {
					get { return this; }
				}

				public override IVariable[] Parameters {
					get { return subprog.Parameters; }
				}

				public override IVariable[] Locals {
					get { return subprog.Locals; }
				}

				public int StartRow {
					get { return start_row; }
				}

				public int EndRow {
					get { return end_row; }
				}

				public ArrayList Addresses {
					get { return addresses; }
				}

				public SourceMethod SourceMethod {
					get { return source; }
				}

				public ISourceBuffer SourceBuffer {
					get { return buffer; }
				}

				void read_source ()
				{
					start_row = end_row = 0;
					addresses = null;

					string file = engine.GetSource (
						subprog, out start_row, out end_row, out addresses);
					if (file == null)
						throw new InternalError ();

					if ((addresses != null) && (addresses.Count > 2)) {
						LineEntry start = (LineEntry) addresses [1];
						LineEntry end = (LineEntry) addresses [addresses.Count - 1];

						SetMethodBounds (start.Address, end.Address);
					}

					source = new DwarfSourceMethod (subprog.SourceFile, this);
					subprog.DieCompileUnit.AddMethod (source);

					buffer = subprog.dwarf.factory.FindFile (subprog.SourceFile.FileName);
				}

				public override SourceMethod GetTrampoline (TargetAddress address)
				{
					return ((ILanguageBackend) subprog.dwarf.bfd).GetTrampoline (address);
				}
			}

			protected class DwarfTargetMethodSource : MethodSource
			{
				DwarfTargetMethod method;

				public DwarfTargetMethodSource (DwarfTargetMethod method, SourceFile file)
					: base (method, file)
				{
					this.method = method;
				}

				protected override MethodSourceData ReadSource ()
				{
					return new MethodSourceData (
						method.StartRow, method.EndRow, method.Addresses,
						method.SourceMethod, method.SourceBuffer);
				}

				public override SourceMethod[] MethodLookup (string query)
				{
					return new SourceMethod [0];
				}
			}

			protected class DwarfSourceMethod : SourceMethod
			{
				IMethod method;

				public DwarfSourceMethod (SourceFile source, DwarfTargetMethod method)
					: base (source, method.Name, method.StartRow, method.EndRow, false)
				{
					this.method = method;
				}

				public override bool IsLoaded {
					get {
						return true;
					}
				}

				public override IMethod Method {
					get {
						return method;
					}
				}

				public override TargetAddress Lookup (int line)
				{
					if (!method.HasSource)
						return TargetAddress.Null;

					return method.Source.Lookup (line);
				}

				public override IDisposable RegisterLoadHandler (MethodLoadedHandler handler,
										 object user_data)
				{
					throw new InvalidOperationException ();
				}
			}
		}

		protected class CompilationUnit
		{
			DwarfReader dwarf;
			long real_start_offset, start_offset, unit_length, abbrev_offset;
			int version, address_size;
			DieCompileUnit comp_unit_die;
			Hashtable abbrevs;
			Hashtable types;

			public CompilationUnit (DwarfReader dwarf, DwarfBinaryReader reader)
			{
				this.dwarf = dwarf;

				real_start_offset = reader.Position;
				unit_length = reader.ReadInitialLength ();
				start_offset = reader.Position;
				version = reader.ReadInt16 ();
				abbrev_offset = reader.ReadOffset ();
				address_size = reader.ReadByte ();

				if (version < 2)
					throw new DwarfException (
						dwarf, String.Format ("Wrong DWARF version: {0}", version));

				abbrevs = new Hashtable ();
				types = new Hashtable ();

				DwarfBinaryReader abbrev_reader = dwarf.DebugAbbrevReader;

				abbrev_reader.Position = abbrev_offset;
				while (abbrev_reader.PeekByte () != 0) {
					AbbrevEntry entry = new AbbrevEntry (dwarf, abbrev_reader);
					abbrevs.Add (entry.ID, entry);
				}

				comp_unit_die = Die.CreateDieCompileUnit (reader, this);

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
					return DieCompileUnit.SymbolTable;
				}
			}

			internal long RealStartOffset {
				get {
					return real_start_offset;
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

			public void AddType (long offset, DieType type)
			{
				types.Add (offset, type);
			}

			public DieType GetType (long offset)
			{
				return (DieType) types [real_start_offset + offset];
			}

			public override string ToString ()
			{
				return String.Format ("CompilationUnit ({0},{1},{2} - {3},{4},{5})",
						      dwarf.Is64Bit ? "64-bit" : "32-bit", version,
						      address_size, start_offset, unit_length,
						      abbrev_offset);
			}
		}

		protected class TargetVariable : IVariable
		{
			string name;
			NativeType type;
			TargetBinaryReader location;

			public TargetVariable (string name, NativeType type, TargetBinaryReader location)
			{
				this.name = name;
				this.type = type;
				this.location = location;
			}

			public string Name {
				get { return name; }
			}

			ITargetType IVariable.Type {
				get { return type; }
			}

			public NativeType Type {
				get { return type; }
			}

			public bool IsAlive (TargetAddress address)
			{
				return true;
			}

			public bool CheckValid (StackFrame frame)
			{
				return true;
			}

			protected TargetLocation GetAddress (StackFrame frame)
			{
				location.Position = 0;
				switch (location.ReadByte ()) {
				case 0x91: // DW_OP_fbreg
					int offset = location.ReadSLeb128 ();

					if (!location.IsEof)
						return null;

					return new MonoVariableLocation (frame, true, (int) I386Register.EBP,
									 offset, type.IsByRef, 0);
				}

				return null;
			}

			public ITargetObject GetObject (StackFrame frame)
			{
				TargetLocation location = GetAddress (frame);
				if (location == null)
					return null;

				return type.GetObject (location);
			}

			public override string ToString ()
			{
				return String.Format ("NativeVariable [{0}:{1}]", Name, Type);
			}
		}

		protected abstract class DieType : Die
		{
			string name;
			long offset;
			bool resolved;
			NativeType type;

			public DieType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.offset = offset;
				comp_unit.AddType (offset, this);
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				}
			}

			protected NativeType GetReference (long offset)
			{
				DieType reference = comp_unit.GetType (offset);
				if ((reference == null) || !reference.HasType)
					return null;

				return reference.Type;
			}

			protected NativeType Resolve ()
			{
				if (!resolved) {
					type = DoResolve ();
					if (name == null) {
						if (type != null)
							name = type.Name;
						else
							name = "void";
					}
					resolved = true;
				}
				return type;
			}

			protected abstract NativeType DoResolve ();

			public bool HasType {
				get {
					Resolve ();
					return type != null;
				}
			}
			
			public NativeType Type {
				get {
					if (!HasType)
						return NativeType.VoidType;
					else
						return type;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			internal void SetName (string name)
			{
				if (resolved)
					throw new InvalidOperationException ();

				this.name = name;
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3})", GetType (),
						      offset, Name, Type);
			}
		}

		protected class DieBaseType : DieType
		{
			int byte_size;
			int encoding;
			Type type;

			public DieBaseType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					    long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.encoding:
					encoding = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override NativeType DoResolve ()
			{
				type = GetMonoType ((DwarfBaseTypeEncoding) encoding, byte_size);
				if (type != null) {
					if (Name == null)
						SetName (type.Name);
					return new NativeFundamentalType (Name, type, byte_size);
				} else
					return new NativeOpaqueType (Name, byte_size);
			}

			protected Type GetMonoType (DwarfBaseTypeEncoding encoding, int byte_size)
			{
				switch (encoding) {
				case DwarfBaseTypeEncoding.signed:
					if (byte_size == 1)
						return typeof (sbyte);
					else if (byte_size == 2)
						return typeof (short);
					else if (byte_size <= 4)
						return typeof (int);
					else if (byte_size <= 8)
						return typeof (long);
					break;

				case DwarfBaseTypeEncoding.unsigned:
					if (byte_size == 1)
						return typeof (byte);
					else if (byte_size == 2)
						return typeof (ushort);
					else if (byte_size <= 4)
						return typeof (uint);
					else if (byte_size <= 8)
						return typeof (ulong);
					break;

				case DwarfBaseTypeEncoding.signed_char:
				case DwarfBaseTypeEncoding.unsigned_char:
					if (byte_size <= 2)
						return typeof (char);
					break;

				case DwarfBaseTypeEncoding.normal_float:
					if (byte_size <= 4)
						return typeof (float);
					else if (byte_size <= 8)
						return typeof (double);
					break;
				}

				return null;
			}

			public Type MonoType {
				get { return type; }
			}
		}

		protected class DiePointerType : DieType
		{
			int byte_size;
			long type_offset;

			public DiePointerType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					       long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override NativeType DoResolve ()
			{
				NativeType ref_type = GetReference (type_offset);
				if (ref_type == null) {
					Console.WriteLine ("UNKNOWN POINTER: {0}", comp_unit.RealStartOffset + type_offset);
					return null;
				}

				if (ref_type.TypeHandle == typeof (char))
					return new NativeStringType (byte_size);

				if (Name == null)
					SetName (String.Format ("{0} *", ref_type.Name));

				return new NativePointerType (Name, ref_type, byte_size);
			}
		}

		protected class DieConstType : DieType
		{
			long type_offset;

			public DieConstType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					     long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override NativeType DoResolve ()
			{
				return GetReference (type_offset);
			}
		}

		protected class DieTypedef : DieType
		{
			long type_offset;

			public DieTypedef (DwarfBinaryReader reader, CompilationUnit comp_unit,
					   long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override NativeType DoResolve ()
			{
				if (Name == null)
					throw new InvalidOperationException ();

				DieType reference = comp_unit.GetType (type_offset);
				if (reference == null)
					return null;

				reference.SetName (Name);
				if (!reference.HasType)
					return null;

				return reference.Type;
			}
		}

		protected class DieStructureType : DieType
		{
			int byte_size;

			public DieStructureType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						 long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			NativeStructType type;

			protected override NativeType DoResolve ()
			{
				if (type != null)
					return type;
				if (Name == null)
					throw new InternalError ();

				type = new NativeStructType (Name, byte_size);

				ArrayList field_list = new ArrayList ();

				if (abbrev.HasChildren) {
					foreach (Die child in Children) {
						DieMember member = child as DieMember;
						if ((member == null) || !member.Resolve ())
							continue;

						NativeFieldInfo field = new NativeFieldInfo (
							member.Type, member.Name, field_list.Count, member.DataOffset);
						field_list.Add (field);
					}
				}

				NativeFieldInfo[] fields = new NativeFieldInfo [field_list.Count];
				field_list.CopyTo (fields, 0);

				type.SetFields (fields);
				return type;
			}
		}

		protected class DieSubroutineType : DieType
		{
			long type_offset;
			bool prototyped;
			NativeType return_type;

			public DieSubroutineType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						  long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.prototyped:
					prototyped = (bool) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.formal_parameter:
					return new DieFormalParameter (null, reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			protected override NativeType DoResolve ()
			{
				if (!prototyped)
					return null;

				if (type_offset != 0) {
					DieType reference = comp_unit.GetType (type_offset);
					if ((reference == null) || !reference.HasType)
						return null;

					return_type = reference.Type;
				}

				ArrayList args = new ArrayList ();

				if (abbrev.HasChildren) {
					foreach (Die child in Children) {
						DieFormalParameter formal = child as DieFormalParameter;
						if (formal == null)
							return null;

						args.Add (formal);
					}
				}

				NativeType[] param_types = new NativeType [0];
				NativeFunctionType func_type = new NativeFunctionType ("test", return_type, param_types);

				return func_type;
			}
		}

		protected abstract class DieVariableBase : Die
		{
			string name;
			long type_offset;

			public DieVariableBase (DwarfBinaryReader reader, CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			public long TypeOffset {
				get {
					return type_offset;
				}
			}
		}

		protected abstract class DieMethodVariable : DieVariableBase
		{
			public DieMethodVariable (DieSubprogram parent, DwarfBinaryReader reader,
						  CompilationUnit comp_unit, AbbrevEntry abbrev, bool is_local)
				: base (reader, comp_unit, abbrev)
			{
				this.target_info = reader.TargetInfo;

				if (parent != null) {
					if (is_local)
						parent.AddLocal (this);
					else
						parent.AddParameter (this);
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.location:
					location = (byte []) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			byte[] location;
			TargetVariable variable;
			ITargetInfo target_info;
			bool resolved;

			protected bool DoResolve ()
			{
				if ((TypeOffset == 0) || (location == null) || (Name == null))
					return false;

				DieType type = comp_unit.GetType (TypeOffset);
				if (type == null)
					return false;

				TargetBinaryReader locreader = new TargetBinaryReader (location, target_info);
				variable = new TargetVariable (Name, type.Type, locreader);
				return true;
			}

			public TargetVariable Variable {
				get {
					if (!resolved) {
						DoResolve ();
						resolved = true;
					}

					return variable;
				}
			}
		}

		protected class DieFormalParameter : DieMethodVariable
		{
			public DieFormalParameter (DieSubprogram parent, DwarfBinaryReader reader,
						   CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (parent, reader, comp_unit, abbrev, false)
			{ }
		}

		protected class DieVariable : DieMethodVariable
		{
			public DieVariable (DieSubprogram parent, DwarfBinaryReader reader,
					    CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (parent, reader, comp_unit, abbrev, true)
			{ }
		}

		protected class DieMember : DieVariableBase
		{
			public DieMember (DwarfBinaryReader reader, CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.target_info = reader.TargetInfo;
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.data_member_location:
					location = (byte []) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			byte[] location;
			bool resolved, ok;
			NativeType type;
			ITargetInfo target_info;
			int offset;

			public bool Resolve ()
			{
				if (resolved)
					return ok;

				ok = DoResolve ();
				return ok;
			}

			bool read_location (TargetBinaryReader locreader)
			{
				switch (locreader.ReadByte ()) {
				case 0x23: // DW_OP_plus_uconstant
					offset = locreader.ReadLeb128 ();
					return locreader.IsEof;

				default:
					return false;
				}
			}

			protected bool DoResolve ()
			{
				if ((TypeOffset == 0) || (location == null) || (Name == null))
					return false;

				DieType type_die = comp_unit.GetType (TypeOffset);
				if (type_die == null)
					return false;

				if ((type_die == null) || !type_die.HasType)
					return false;

				type = type_die.Type;

				TargetBinaryReader locreader = new TargetBinaryReader (location, target_info);
				if (!read_location (locreader))
					return false;

				return true;
			}

			public NativeType Type {
				get {
					Resolve ();
					return type;
				}
			}

			public int DataOffset {
				get {
					Resolve ();
					return offset;
				}
			}
		}
	}
}
