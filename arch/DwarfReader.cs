using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;

namespace Mono.Debugger.Architecture
{
	internal class DwarfException : Exception
	{
		public DwarfException (Bfd bfd, string message, params object[] args)
			: base (String.Format ("{0}: {1}", bfd.FileName,
					       String.Format (message, args)))
		{ }

		public DwarfException (Bfd bfd, string message, Exception inner)
			: base (String.Format ("{0}: {1}", bfd.FileName, message), inner)
		{ }
	}

	internal class DwarfBinaryReader : TargetBinaryReader
	{
		Bfd bfd;
		bool is64bit;

		public DwarfBinaryReader (Bfd bfd, TargetBlob blob, bool is64bit)
			: base (blob)
		{
			this.bfd = bfd;
			this.is64bit = is64bit;
		}

		public Bfd Bfd {
			get {
				return bfd;
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
				throw new DwarfException (
					bfd, "Unknown initial length field {0:x}",
					length);
		}
	}

	internal class DwarfReader : ISymbolFile
	{
		protected Bfd bfd;
		protected Module module;
		protected string filename;
		bool is64bit;
		byte address_size;
		int frame_register;

		ObjectCache debug_info_reader;
		ObjectCache debug_abbrev_reader;
		ObjectCache debug_line_reader;
		ObjectCache debug_aranges_reader;
		ObjectCache debug_pubnames_reader;
		ObjectCache debug_str_reader;

		Hashtable source_hash;
		Hashtable method_source_hash;
		Hashtable method_hash;
		Hashtable compile_unit_hash;
		Hashtable type_hash;
		DwarfSymbolTable symtab;
		ArrayList aranges;
		Hashtable pubnames;
		ITargetInfo target_info;
		SourceFileFactory factory;

		public DwarfReader (Bfd bfd, Module module, SourceFileFactory factory)
		{
			this.bfd = bfd;
			this.module = module;
			this.filename = bfd.FileName;
			this.factory = factory;
			this.target_info = bfd.TargetInfo;

			if (bfd.Target == "elf32-i386")
				frame_register = (int) I386Register.EBP;
			else if (bfd.Target == "elf64-x86-64")
				frame_register = (int) X86_64_Register.RBP;
			else
				throw new DwarfException (
					bfd, "Unknown architecture: {0}", bfd.Target);

			debug_info_reader = create_reader (".debug_info");

			DwarfBinaryReader reader = DebugInfoReader;

			reader.ReadInitialLength (out is64bit);
			int version = reader.ReadInt16 ();
			if (version < 2)
				throw new DwarfException (
					bfd, "Wrong DWARF version: {0}", version);

			reader.ReadOffset ();
			address_size = reader.ReadByte ();

			if ((address_size != 4) && (address_size != 8))
				throw new DwarfException (
					bfd, "Unknown address size: {0}", address_size);

			debug_abbrev_reader = create_reader (".debug_abbrev");
			debug_line_reader = create_reader (".debug_line");
			debug_aranges_reader = create_reader (".debug_aranges");
			debug_pubnames_reader = create_reader (".debug_pubnames");
			debug_str_reader = create_reader (".debug_str");

			compile_unit_hash = Hashtable.Synchronized (new Hashtable ());
			method_source_hash = Hashtable.Synchronized (new Hashtable ());
			method_hash = Hashtable.Synchronized (new Hashtable ());
			source_hash = Hashtable.Synchronized (new Hashtable ());
			type_hash = Hashtable.Synchronized (new Hashtable ());

			if (bfd.IsLoaded) {
				aranges = ArrayList.Synchronized (read_aranges ());
				symtab = new DwarfSymbolTable (this, aranges);
				pubnames = Hashtable.Synchronized (read_pubnames ());
			}

			long offset = 0;
			while (offset < reader.Size) {
				CompileUnitBlock block = new CompileUnitBlock (this, offset);
				compile_unit_hash.Add (offset, block);
				offset += block.length;
			}
		}

		public void ModuleLoaded ()
		{
			if (aranges != null)
				return;

			aranges = ArrayList.Synchronized (read_aranges ());
			symtab = new DwarfSymbolTable (this, aranges);

			pubnames = Hashtable.Synchronized (read_pubnames ());
		}

		public static bool IsSupported (Bfd bfd)
		{
			if ((bfd.Target == "elf32-i386") || (bfd.Target == "elf64-x86-64"))
				return bfd.HasSection (".debug_info");
			else
				return false;
		}

		public ITargetInfo TargetInfo {
			get {
				return target_info;
			}
		}

		protected TargetAddress GetAddress (long address)
		{
			if (!bfd.IsLoaded)
				throw new InvalidOperationException (
					"Trying to get an address from not-loaded " +
					"symbol file `" + bfd.FileName + "'");

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
				SourceFile[] retval = new SourceFile [source_hash.Count];
				source_hash.Keys.CopyTo (retval, 0);
				return retval;
			}
		}

		IMethod ISymbolFile.GetMethod (long handle)
		{
			DwarfTargetMethod method = (DwarfTargetMethod) method_hash [handle];
			if ((method == null) || !method.CheckLoaded ())
				return null;
			return method;
		}

		void ISymbolFile.GetMethods (SourceFile file)
		{
			DieCompileUnit die = (DieCompileUnit) source_hash [file];

			foreach (Die child in die.Subprograms) {
				DieSubprogram subprog = child as DieSubprogram;
				if (subprog == null)
					continue;
			}
		}

		public SourceMethod FindMethod (string name)
		{
			if (pubnames == null)
				return null;

			NameEntry entry = (NameEntry) pubnames [name];
			if (entry == null)
				return null;

			SourceMethod source;
			source = (SourceMethod) method_source_hash [entry.AbsoluteOffset];
			if (source != null)
				return source;

			CompileUnitBlock block = (CompileUnitBlock) compile_unit_hash [entry.FileOffset];
			return block.GetMethod (entry.AbsoluteOffset);
		}

		protected SourceMethod GetSourceMethod (DieSubprogram subprog,
							int start_row, int end_row)
		{
			SourceMethod source;
			source = (SourceMethod) method_source_hash [subprog.Offset];
			if (source != null)
				return source;

			source = new SourceMethod (
				module, subprog.SourceFile, subprog.Offset, subprog.Name,
				start_row, end_row, true);
			method_source_hash.Add (subprog.Offset, source);
			return source;
		}

		protected SourceFile AddSourceFile (DieCompileUnit die, string filename)
		{
			SourceFile file = new SourceFile (module, filename);
			source_hash.Add (file, die);
			return file;
		}

		protected void AddType (string name, DieType type)
		{
			if (!type_hash.Contains (name))
				type_hash.Add (name, type);
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			DieType type = (DieType) type_hash [name];
			if (type == null)
				return null;

			return type.ResolveType ();
		}

		protected class CompileUnitBlock
		{
			public readonly DwarfReader dwarf;
			public readonly long offset;
			public readonly long length;

			SymbolTableCollection symtabs;
			ArrayList compile_units;
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

			CompilationUnit get_comp_unit (long offset)
			{
				foreach (CompilationUnit comp_unit in compile_units) {
					long start = comp_unit.RealStartOffset;
					long end = start + comp_unit.UnitLength;

					if ((offset >= start) && (offset < end))
						return comp_unit;
				}

				return null;
			}

			public SourceMethod GetMethod (long offset)
			{
				build_symtabs ();
				CompilationUnit comp_unit = get_comp_unit (offset);
				if (comp_unit == null)
					return null;

				DieCompileUnit die = comp_unit.DieCompileUnit;
				DieSubprogram subprog = die.GetSubprogram (offset);
				if (subprog == null)
					return null;

				return subprog.SourceMethod;
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
					throw new DwarfException (
						dwarf.bfd, "Wrong DWARF version: {0}", version);

				reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				reader.Position = offset;

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (
						dwarf.bfd, "Unknown address size: {0}",
						address_size);

				compile_units = new ArrayList ();

				while (reader.Position < stop) {
					CompilationUnit comp_unit = new CompilationUnit (dwarf, reader);
					compile_units.Add (comp_unit);
				}
			}

			public override string ToString ()
			{
				return String.Format ("CompileUnitBlock ({0}:{1}:{2})",
						      dwarf.FileName, offset, length);
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
				return String.Format ("{0} ({1}:{2})", GetType (),
						      dwarf.FileName, ranges.Count);
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
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long offset = reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				int segment_size = reader.ReadByte ();

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (
						bfd, "Unknown address size: {0}", address_size);
				if (segment_size != 0)
					throw new DwarfException (
						bfd, "Segmented address mode not supported");

				if (version != 2)
					throw new DwarfException (
						bfd, "Wrong version in .debug_aranges: {0}",
						version);

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

		private class NameEntry
		{
			public readonly long FileOffset;
			public readonly long Offset;

			public long AbsoluteOffset {
				get { return FileOffset + Offset; }
			}

			public NameEntry (long file_offset, long offset)
			{
				this.FileOffset = file_offset;
				this.Offset = offset;
			}

			public override string ToString ()
			{
				return String.Format ("NameEntry ({0}:{1})",
						      FileOffset, Offset);
			}
		}

		Hashtable read_pubnames ()
		{
			DwarfBinaryReader reader = DebugPubnamesReader;

			Hashtable names = new Hashtable ();

			// if the reader comes back null, we just
			// can't look up symbols by name, return an
			// empty Hashtable to reflect this.
			if (reader == null)
				return names;

			while (!reader.IsEof) {
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long debug_offset = reader.ReadOffset ();
				reader.ReadOffset ();

				if (version != 2)
					throw new DwarfException (
						bfd, "Wrong version in .debug_pubnames: {0}",
						version);

				while (reader.Position < stop) {
					long offset = reader.ReadInt32 ();
					if (offset == 0)
						break;

					string name = reader.ReadString ();
					if (!names.Contains (name))
						names.Add (name, new NameEntry (debug_offset, offset));
				}
			}

			return names;
		}

		object create_reader_func (object user_data)
		{
			byte[] contents = bfd.GetSectionContents ((string) user_data, false);

			if (contents == null) {
				Report.Debug (DebugFlags.DwarfReader,
					      "{1} Can't find DWARF 2 debugging info in section `{0}'",
					      bfd.FileName, (string) user_data);

				return null;
			}
			else {
				return new TargetBlob (contents, bfd.TargetInfo);
			}
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
				return new DwarfBinaryReader (
					bfd, (TargetBlob) debug_info_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugAbbrevReader {
			get {
				return new DwarfBinaryReader (
					bfd, (TargetBlob) debug_abbrev_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugPubnamesReader {
			get {
				TargetBlob blob = (TargetBlob) debug_pubnames_reader.Data;
				if (blob == null)
					return null;
				else
					return new DwarfBinaryReader (
							bfd, (TargetBlob) debug_pubnames_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugLineReader {
			get {
				return new DwarfBinaryReader (
					bfd, (TargetBlob) debug_line_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugArangesReader {
			get {
				return new DwarfBinaryReader (
					bfd, (TargetBlob) debug_aranges_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugStrReader {
			get {
				return new DwarfBinaryReader (
					bfd, (TargetBlob) debug_str_reader.Data, Is64Bit);
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
		  //		  Console.WriteLine (String.Format (message, args));
		}

		protected enum DwarfLang {
			C89         = 0x0001,
			C           = 0x0002,
			Ada83       = 0x0003,
			C_plus_plus = 0x0004,
			Cobol74     = 0x0005,
			Cobol85     = 0x0006,
			Fortran77   = 0x0007,
			Fortran90   = 0x0008,
			Pascal83    = 0x0009,
			Modula2     = 0x000a,
			None        = 0x8001
		}

		protected enum DwarfTag {
			array_type		= 0x01,
			class_type		= 0x02,
			entry_point             = 0x03,
			enumeration_type	= 0x04,
			formal_parameter	= 0x05,
			imported_declaration    = 0x08,
			label                   = 0x0a,
			lexical_block           = 0x0b,
			member			= 0x0d,
			pointer_type		= 0x0f,
			reference_type          = 0x10,
			compile_unit		= 0x11,
			string_type             = 0x12,
			structure_type		= 0x13,
			subroutine_type		= 0x15,
			typedef			= 0x16,
			union_type		= 0x17,
			unspecified_parameters  = 0x18,
			variant                 = 0x19,
			common_block            = 0x1a,
			comp_dir		= 0x1b,
			inheritance		= 0x1c,
			inlined_subroutine      = 0x1d,
			module                  = 0x1e,
			ptr_to_member_type      = 0x1f,
			set_type                = 0x20,
			subrange_type		= 0x21,
			with_stmt               = 0x22,
			access_declaration	= 0x23,
			base_type		= 0x24,
			catch_block             = 0x25,
			const_type		= 0x26,
			constant                = 0x27,
			enumerator		= 0x28,
			file_type               = 0x29,
			friend                  = 0x2a,
			namelist                = 0x2b,
			namelist_item           = 0x2c,
			packed_type             = 0x2d,
			subprogram		= 0x2e,
			template_type_param     = 0x2f,
			template_value_param    = 0x30,
			thrown_type             = 0x31,
			try_block               = 0x32,
			variant_block           = 0x33,
			variable		= 0x34,
			volatile_type           = 0x35
		}

		protected enum DwarfAttribute {
			sibling                 = 0x01,
			location	        = 0x02,
			name			= 0x03,
			ordering                = 0x09,
			byte_size		= 0x0b,
			bit_offset		= 0x0c,
			bit_size		= 0x0d,
			stmt_list		= 0x10,
			low_pc			= 0x11,
			high_pc			= 0x12,
			language		= 0x13,
			discr                   = 0x15,
			discr_value             = 0x16,
			visibility              = 0x17,
			import                  = 0x18,
			string_length           = 0x19,
			common_reference        = 0x1a,
			comp_dir		= 0x1b,
			const_value		= 0x1c,
			containing_type         = 0x1d,
			default_value           = 0x1e,
			inline                  = 0x20,
			is_optional             = 0x21,
			lower_bound		= 0x22,
			producer		= 0x25,
			prototyped		= 0x27,
			return_addr             = 0x2a,
			start_scope		= 0x2c,
			stride_size             = 0x2e,
			upper_bound		= 0x2f,
			abstract_origin         = 0x31,
			accessibility		= 0x32,
			address_class           = 0x33,
			artificial		= 0x34,
			base_types              = 0x35,
			calling_convention	= 0x36,
			count			= 0x37,
			data_member_location	= 0x38,
			decl_column             = 0x39,
			decl_file               = 0x3a,
			decl_line               = 0x3b,
			declaration             = 0x3c,
			discr_list              = 0x3d,
			encoding		= 0x3e,
			external		= 0x3f,
			frame_base              = 0x40,
			friend                  = 0x41,
			identifier_case         = 0x42,
			macro_info              = 0x43,
			namelist_item           = 0x44,
			priority                = 0x45,
			segment                 = 0x46,
			specification           = 0x47,
			static_link             = 0x48,
			type			= 0x49,
			use_location            = 0x4a,
			variable_parameter      = 0x4b,
			virtuality		= 0x4c,
			vtable_elem_location	= 0x4d
		}

		protected enum DwarfBaseTypeEncoding {
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

		protected enum DwarfForm {
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
			ref_udata		= 0x15,
			indirect                = 0x16
		}

		protected enum DwarfInline {
			not_inlined             = 0x00,
			inlined                 = 0x01,
			declared_not_inlined    = 0x02,
			declared_inline         = 0x03
		}

		protected struct LineNumber : IComparable
		{
			public readonly long Offset;
			public readonly int Line;

			public LineNumber (long offset, int line)
			{
				this.Offset = offset;
				this.Line = line;
			}

			public int CompareTo (object obj)
			{
				LineNumber entry = (LineNumber) obj;

				if (entry.Offset < Offset)
					return 1;
				else if (entry.Offset > Offset)
					return -1;
				else
					return 0;
			}

			public override string ToString ()
			{
				return String.Format ("LineNumber ({0}:{1})",
						      Line, Offset);
			}
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

			DieSubprogram next_method, current_method;
			long next_method_address;
			int next_method_index;

			int[] standard_opcode_lengths;
			ArrayList include_directories;
			string compilation_dir;
			ArrayList methods;
			Hashtable method_hash;

			protected class StatementMachine
			{
				public LineNumberEngine engine;			      
				public long st_address;
				public int st_line;
				public int st_file;
				public int st_column;
				public bool is_stmt;
				public bool basic_block;
				public bool end_sequence;
				public bool prologue_end;
				public bool epilogue_begin;
				public long start_offset;
				public long end_offset;

				public int start_file;
				public int start_line, end_line;
				public ArrayList lines;

				bool creating;
				DwarfBinaryReader reader;
				int const_add_pc_range;

				public StatementMachine (LineNumberEngine engine, long offset,
							 long end_offset)
				{
					this.engine = engine;
					this.reader = this.engine.reader;
					this.creating = true;
					this.start_offset = offset;
					this.end_offset = end_offset;
					this.st_address = 0;
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
					this.end_offset = stm.end_offset;
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

					engine.debug ("CLONE: {0} {1} {2}",
						      stm.start_line, stm.end_line, stm.st_file);

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
						lines.Add (new LineNumber (st_address, st_line));

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
						st_address = reader.ReadAddress ();
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

					while (!end_sequence && (reader.Position < end_offset)) {
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
					next_method = (DieSubprogram) methods [next_method_index++];
					next_method_address = next_method.StartAddress;
				} else
					next_method_address = 0;

				debug ("NEW NEXT: {0:x}", next_method_address);
			}

			void commit (StatementMachine stm)
			{
				debug ("COMMIT: {0:x} {1} {2:x}", stm.st_address, stm.st_line,
				       next_method_address);

				if ((next_method_address > 0) && (stm.st_address >= next_method_address))
					end_sequence (stm);
			}

			void warning (string message)
			{
				Console.WriteLine (message);
			}

			void error (string message)
			{
				throw new DwarfException (dwarf.bfd, message);
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
					next_method = (DieSubprogram) methods [0];
					next_method_address = next_method.StartAddress;
				}

				method_hash = new Hashtable ();

				StatementMachine stm = new StatementMachine (this, data_offset, end_offset);
				stm.Read ();

				if ((current_method != null) && !method_hash.Contains (current_method))
					method_hash.Add (current_method, new StatementMachine (stm));
			}

			public string GetSource (DieSubprogram method, out int start_row, out int end_row,
						 out LineNumber[] lines)
			{
				start_row = end_row = 0;
				lines = null;

				StatementMachine stm = (StatementMachine) method_hash [method];
				if ((stm == null) || (stm.st_file == 0))
					return null;

				FileEntry file = (FileEntry) source_files [stm.st_file - 1];
				start_row = stm.start_line;
				end_row = stm.end_line;

#if FIXME
				addresses = new LineEntry [stm.lines.Count];
				for (int i = 0; i < addresses.Length; i++) {
					LineNumber line = (LineNumber) stm.lines [i];
					     addresses [i] = new LineEntry (
						     dwarf.GetAddress (line.Offset), line.Line);
				}
#endif

				lines = new LineNumber [stm.lines.Count];
				stm.lines.CopyTo (lines, 0);

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
					throw new DwarfException (
						dwarf.bfd, "Unknown DW_FORM: 0x{0:x}",
						(int) form);
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
						dwarf.bfd, "Unknown DW_FORM: 0x{0:x}",
						(int) form);
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
			int abbrev_id;
			DwarfTag tag;
			bool has_children;

			public readonly ArrayList Attributes;

			public AbbrevEntry (DwarfReader dwarf, DwarfBinaryReader reader)
			{
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

		// <summary>
		// Base class for all DIE's - The DWARF Debugging Information Entry.
		// </summary>
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
					return new DieSubprogram (reader, comp_unit, offset, abbrev);

				case DwarfTag.base_type:
					return new DieBaseType (reader, comp_unit, offset, abbrev);

				case DwarfTag.const_type:
					return new DieConstType (reader, comp_unit, offset, abbrev);

				case DwarfTag.pointer_type:
					return new DiePointerType (reader, comp_unit, offset, abbrev);

				case DwarfTag.class_type: // for now just treat classes and structs the same.
				case DwarfTag.structure_type:
					return new DieStructureType (reader, comp_unit, offset, abbrev, false);

				case DwarfTag.union_type:
					return new DieStructureType (reader, comp_unit, offset, abbrev, true);

				case DwarfTag.array_type:
					return new DieArrayType (reader, comp_unit, offset, abbrev);

				case DwarfTag.subrange_type:
					return new DieSubrangeType (reader, comp_unit, abbrev);

				case DwarfTag.enumeration_type:
					return new DieEnumerationType (reader, comp_unit, offset, abbrev);

				case DwarfTag.enumerator:
					return new DieEnumerator (reader, comp_unit, abbrev);

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

		// <summary>
		// The Debugging Information Entry corresponding to compilation units.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>A compilation unit typically
		// represents the text and data contributed to an executable
		// by a single relocatable object file.  It may be derived
		// from several source files, including pre-processed
		// ``include files.''</em>
		// </remarks>
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
				file = dwarf.AddSourceFile (this, file_name);
			}

			long start_pc, end_pc;
			string name;
			string comp_dir;
			bool is_continuous;
			DwarfLang language;
			SourceFile file;
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

			void read_symtab ()
			{
				if ((symtab != null) || !dwarf.bfd.IsLoaded)
					return;

				symtab = new CompileUnitSymbolTable (this);
			}

			protected LineNumberEngine Engine {
				get {
					read_children ();
					return engine;
				}
			}

			public ArrayList Subprograms {
				get {
					read_children ();
					return children;
				}
			}

			public DieSubprogram GetSubprogram (long offset)
			{
				read_children ();
				foreach (DieSubprogram subprog in children) {
					if (subprog.RealOffset == offset)
						return subprog;
				}

				return null;
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

				case DwarfAttribute.language:
#if FIXME
					language = (DwarfLang)attribute.Data;
					Console.WriteLine ("DieCompileUnit {0} has language {1}", name, language);
#endif
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
					read_symtab ();
					return symtab;
				}
			}

			TargetAddress ISymbolContainer.StartAddress {
				get {
					return dwarf.GetAddress (StartAddress);
				}
			}

			TargetAddress ISymbolContainer.EndAddress {
				get {
					return dwarf.GetAddress (EndAddress);
				}
			}

			public long StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return start_pc;
				}
			}

			public long EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return end_pc;
				}
			}

			public SourceFile SourceFile {
				get {
					return file;
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

					foreach (DieSubprogram subprog in list)
						methods.Add (subprog.Method);

					return methods;
				}
			}
		}

		// <summary>
		// The Debugging Information Entry corresponding to a
		// subprogram, which in most languages means a method or
		// function or subroutine.
		// </summary>
		protected class DieSubprogram : Die, IComparable, ISymbolContainer
		{
			long real_offset, start_pc, end_pc;
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

				case DwarfAttribute.decl_file:
					//Console.WriteLine ("decl_file = {0}", (long) attribute.Data);
					break;

				case DwarfAttribute.decl_line:
					//Console.WriteLine ("decl_line = {0}", (long) attribute.Data);
					break;

				case DwarfAttribute.inline:
					//Console.WriteLine ("inline = {0}", (DwarfInline) (long)attribute.Data);
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
					      long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.real_offset = offset;
				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;
			}

			public SourceFile SourceFile {
				get {
					return DieCompileUnit.SourceFile;
				}
			}

			public SourceMethod SourceMethod {
				get {
					if (method == null)
						return null;

					return method.SourceMethod;
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

			TargetAddress ISymbolContainer.StartAddress {
				get {
					return dwarf.GetAddress (StartAddress);
				}
			}

			TargetAddress ISymbolContainer.EndAddress {
				get {
					return dwarf.GetAddress (EndAddress);
				}
			}

			public long StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return start_pc;
				}
			}

			public long EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return end_pc;
				}
			}

			internal long RealOffset {
				get {
					return real_offset;
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
		}

		protected class DwarfTargetMethodSource : MethodSource
		{
			DwarfTargetMethod method;

			public DwarfTargetMethodSource (DwarfTargetMethod method,
							SourceFile file)
				: base (method, file)
			{
				this.method = method;
			}

			protected override MethodSourceData ReadSource ()
			{
				LineNumber[] lines = method.LineNumbers;
				LineEntry[] addresses = new LineEntry [lines.Length];
				for (int i = 0; i < lines.Length; i++) {
					LineNumber line = lines [i];
					addresses [i] = new LineEntry (
						method.DwarfReader.GetAddress (line.Offset),
						line.Line);
				}

				return new MethodSourceData (
					method.StartRow, method.EndRow, addresses,
					method.SourceMethod, method.SourceBuffer,
					method.Module);
			}
		}

		protected class DwarfTargetMethod : MethodBase
		{
			LineNumberEngine engine;
			DieSubprogram subprog;
			SourceMethod source;
			DwarfTargetMethodSource msource;
			int start_row, end_row;
			LineNumber[] lines;
			ISourceBuffer buffer;

			public DwarfTargetMethod (DieSubprogram subprog, LineNumberEngine engine)
				: base (subprog.Name, subprog.ImageFile, subprog.dwarf.module)
			{
				this.subprog = subprog;
				this.engine = engine;

				read_source ();
				CheckLoaded ();
			}

			public DwarfReader DwarfReader {
				get { return subprog.dwarf; }
			}

			public override object MethodHandle {
				get { return this; }
			}

			public override ITargetStructType DeclaringType {
				get { return null; }
			}

			public override bool HasThis {
				get { return false; }
			}

			public override IVariable This {
				get {
					throw new InvalidOperationException ();
				}
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

			public LineNumber[] LineNumbers {
				get { return lines; }
			}

			public SourceMethod SourceMethod {
				get { return source; }
			}

			public ISourceBuffer SourceBuffer {
				get { return buffer; }
			}

			void read_source ()
			{
				string file = engine.GetSource (
					subprog, out start_row, out end_row, out lines);
				if (file == null)
					throw new InternalError ();

				source = subprog.dwarf.GetSourceMethod (
					subprog, StartRow, EndRow);

				buffer = subprog.dwarf.factory.FindFile (
					subprog.SourceFile.FileName);

				subprog.dwarf.method_hash.Add (source.Handle, this);
			}

			public bool CheckLoaded ()
			{
				if (!subprog.dwarf.bfd.IsLoaded)
					return false;
				if (msource != null)
					return true;

				ISymbolContainer sc = (ISymbolContainer) subprog;
				if (sc.IsContinuous)
					SetAddresses (sc.StartAddress, sc.EndAddress);

				msource = new DwarfTargetMethodSource (
					this, subprog.SourceFile);
				SetSource (msource);

				if ((lines != null) && (lines.Length > 2)) {
					LineNumber start = lines [1];
					LineNumber end = lines [lines.Length - 1];

					SetMethodBounds (
						subprog.dwarf.GetAddress (start.Offset),
						subprog.dwarf.GetAddress (end.Offset));
				}

				return true;
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory, TargetAddress address)
			{
				return ((ILanguageBackend) subprog.dwarf.bfd).GetTrampoline (memory, address);
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
						dwarf.bfd, "Wrong DWARF version: {0}",
						version);

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

			internal long UnitLength {
				get {
					return unit_length;
				}
			}

			public AbbrevEntry this [int abbrev_id] {
				get {
					if (abbrevs.Contains (abbrev_id))
						return (AbbrevEntry) abbrevs [abbrev_id];

					throw new DwarfException (
						dwarf.bfd, "{0} does not contain an " +
						"abbreviation entry {1}", this, abbrev_id);
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
						      address_size, real_start_offset,
						      unit_length, abbrev_offset);
			}
		}

		protected class TargetVariable : IVariable
		{
			DwarfReader dwarf;
			string name;
			NativeType type;
			TargetBinaryReader location;
			int offset;

			public TargetVariable (DwarfReader dwarf, string name, NativeType type,
					       TargetBinaryReader location)
			{
				this.dwarf = dwarf;
				this.name = name;
				this.type = type;
				this.location = location;
			}

			public TargetVariable (DwarfReader dwarf, string name, NativeType type,
					       int offset)
			{
				this.dwarf = dwarf;
				this.name = name;
				this.type = type;
				this.offset = offset;
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

			public TargetLocation GetLocation (StackFrame frame)
			{
				int off;

				if (location != null) {
					location.Position = 0;
					switch (location.ReadByte ()) {
					case 0x91: // DW_OP_fbreg
						off = location.ReadSLeb128 ();

						if (!location.IsEof)
							return null;

						break;
					default:
						return null;
					}
				}
				else {
					off = offset;
				}

				return new MonoVariableLocation (frame, true, dwarf.frame_register,
								 off, type.IsByRef);
			}

			public ITargetObject GetObject (StackFrame frame)
			{
				TargetLocation location = GetLocation (frame);
				if (location == null)
					return null;

				return type.GetObject (location);
			}

			public bool CanWrite {
				get { return type.Kind == TargetObjectKind.Fundamental; }
			}

			public void SetObject (StackFrame frame, ITargetObject obj)
			{
				if (obj.TypeInfo.Type != Type)
					throw new InvalidOperationException ();

				NativeFundamentalObject var_object = GetObject (frame) as NativeFundamentalObject;
				if (var_object == null)
					return;

				var_object.SetObject (obj);
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
			bool resolved, ok, type_created;
			NativeType type;

			public DieType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.offset = offset;
				comp_unit.AddType (offset, this);

				if (name != null)
					comp_unit.DwarfReader.AddType (name, this);
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				}
			}

			protected DieType GetReference (long offset)
			{
				return comp_unit.GetType (offset);
			}

			public NativeType ResolveType ()
			{
				if (resolved)
					return type;

				type = CreateType ();
				resolved = true;

				if (type == null) {
					type_created = true;
					return null;
				}

				if (!type_created) {
					type_created = true;
					PopulateType ();
				}

				return type;
			}

			protected abstract NativeType CreateType ();

			protected virtual void PopulateType ()
			{ }

			public bool HasType {
				get {
					if (!resolved || !ok)
						throw new InvalidOperationException ();
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

			protected void SetName (string name)
			{
				if (resolved)
					throw new InvalidOperationException ();

				this.name = name;
			}

			internal NativeType GetAlias (string name)
			{
				if (this.name == null) {
					this.name = name;
					return Type;
				} else
					return new NativeTypeAlias (name, this.name, Type);
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3})", GetType (),
						      offset, Name, type);
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to base types.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>A base type is a data type that
		// is not defined in terms of other data types.  Each
		// programming language has a set of base types that are
		// considered to be built into that language. </em>
		// </remarks>
		protected class DieBaseType : DieType
		{
			int byte_size;
			int encoding;
			FundamentalKind kind;

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

			protected override NativeType CreateType ()
			{
				kind = GetMonoType (
					(DwarfBaseTypeEncoding) encoding, byte_size);

				if (kind == FundamentalKind.Unknown)
					return new NativeOpaqueType (Name, byte_size);

				return new NativeFundamentalType (Name, kind, byte_size);
			}

			protected FundamentalKind GetMonoType (DwarfBaseTypeEncoding encoding,
							       int byte_size)
			{
				switch (encoding) {
				case DwarfBaseTypeEncoding.signed:
					if (byte_size == 1)
						return FundamentalKind.SByte;
					else if (byte_size == 2)
						return FundamentalKind.Int16;
					else if (byte_size <= 4)
						return FundamentalKind.Int32;
					else if (byte_size <= 8)
						return FundamentalKind.Int64;
					break;

				case DwarfBaseTypeEncoding.unsigned:
					if (byte_size == 1)
						return FundamentalKind.Byte;
					else if (byte_size == 2)
						return FundamentalKind.UInt16;
					else if (byte_size <= 4)
						return FundamentalKind.UInt32;
					else if (byte_size <= 8)
						return FundamentalKind.UInt64;
					break;

				case DwarfBaseTypeEncoding.signed_char:
				case DwarfBaseTypeEncoding.unsigned_char:
					if (byte_size <= 2)
						return FundamentalKind.Char;
					break;

				case DwarfBaseTypeEncoding.normal_float:
					if (byte_size <= 4)
						return FundamentalKind.Single;
					else if (byte_size <= 8)
						return FundamentalKind.Double;
					break;
				}

				return FundamentalKind.Unknown;
			}
		}

		protected class DiePointerType : DieType
		{
			int byte_size;
			long type_offset;
			DieType reference;

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

			protected override NativeType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null) {
					Console.WriteLine (
						"UNKNOWN POINTER: {0}",
						comp_unit.RealStartOffset + type_offset);
					return null;
				}

				NativeType ref_type = reference.ResolveType ();
				if (ref_type == null)
					return null;

				NativeFundamentalType fundamental = ref_type as NativeFundamentalType;
				if ((fundamental != null) &&
				    (fundamental.FundamentalKind == FundamentalKind.Char))
					return new NativeStringType (byte_size);

				string name;
				if (Name != null)
					name = Name;
				else
					name = String.Format ("{0}*", ref_type.Name);

				return new NativePointerType (name, ref_type, byte_size);
			}
		}

		protected class DieSubrangeType : Die
		{
			int upper_bound;
			int lower_bound;

			public DieSubrangeType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.upper_bound:
					upper_bound = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.lower_bound:
					lower_bound = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.count:
					lower_bound = 0;
				  	upper_bound = ((int) (long) attribute.Data) - 1;
					break;
				}
			}

		  	public int UpperBound {
				get {
					return upper_bound;
				}
			}

			public int LowerBound {
				get {
					return lower_bound;
				}
			}
		}

		protected class DieArrayType : DieType
		{
			int ordering;
			int byte_size;
			int stride_size;
			long type_offset;
			DieType reference;

			public DieArrayType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					     long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.stride_size:
					stride_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.ordering:
					ordering = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public int Ordering {
				get { return ordering; }
			}

			public int ByteSize {
				get { return byte_size; }
			}

			public int StrideSize {
				get { return stride_size; }
			}

			protected override NativeType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null) {
					Console.WriteLine (
						"UNKNOWN POINTER: {0}",
						comp_unit.RealStartOffset + type_offset);
					return null;
				}

				NativeType ref_type = reference.ResolveType ();
				if (ref_type == null)
					return null;

#if false
				/* not sure we want this */
				if (ref_type.TypeHandle == typeof (char))
					return new NativeStringType (byte_size);
#endif

				string name;
				if (Name != null)
					name = Name;
				else
					name = String.Format ("{0} []", ref_type.Name);

				/* XXX for now just find the first
				 * Subrange child and use that for the
				 * array dimensions.  This should
				 * really support multidimensional
				 * arrays */
				DieSubrangeType subrange = null;
				foreach (Die d in Children) {
					subrange = d as DieSubrangeType;
					if (subrange == null) continue;
					break;
				}

				return new NativeArrayType (name, ref_type,
							    subrange.LowerBound, subrange.UpperBound, byte_size);
			}
		}

		protected class DieEnumerator : Die
		{
			string name;
			int const_value;

			public DieEnumerator (DwarfBinaryReader reader, CompilationUnit comp_unit,
					      AbbrevEntry abbrev)
			  : base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				case DwarfAttribute.const_value:
					const_value = (int) (long) attribute.Data;
					break;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			public int ConstValue {
				get {
					return const_value;
				}
			}
		}

		protected class DieEnumerationType : DieType
		{
			int byte_size;

			public DieEnumerationType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						   long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.specification:
					Console.WriteLine ("ugh, specification");
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override NativeType CreateType ()
			{
				int num_elements = 0;
				string name;

				foreach (Die d in Children)
					if (d is DieEnumerator) num_elements ++;

				if (Name != null)
					name = Name;
				else
					name = "<unknown enum>";

				string[] names = new string [num_elements];
				int[] values = new int [num_elements];

				int i = 0;
				foreach (Die d in Children) {
					DieEnumerator e = d as DieEnumerator;
					if (e == null) continue;

					names[i] = e.Name;
					values[i] = e.ConstValue;
					i++;
				}

				return new NativeEnumType (name, byte_size, names, values);
			}
		}

		protected class DieConstType : DieType
		{
			long type_offset;
			DieType reference;

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

			protected override NativeType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null)
					return null;

				return reference.ResolveType ();
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to arbitrary
		// types that are assigned names by the programmer.
		// </summary>
		protected class DieTypedef : DieType
		{
			long type_offset;
			DieType reference;
			new NativeTypeAlias type;

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

			protected override NativeType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null)
					return null;

				type = new NativeTypeAlias (Name, reference.Name);
				return type;
			}

			protected override void PopulateType ()
			{
				type.TargetType = reference.ResolveType ();
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to
		// inheritance information.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>The class type of
		// structure type entry that describes a derived class
		// or structure owns debugging information entries
		// describing each of the classes or structures it is
		// derived from, ordered as they were in the source
		// program.</em>
		// </remarks>
		protected class DieInheritance : Die
		{
			long type_offset;
			long data_member_location;
			DieType reference;

			public DieInheritance (DwarfBinaryReader reader,
					       CompilationUnit comp_unit,
					       AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.data_member_location:
					// the location is specified as a
					// block, it appears..  not sure the
					// format, but it definitely isn't a (long).
					//
					// data_member_location = (long)attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public long TypeOffset {
				get { return type_offset; }
			}
		}

		protected class DieStructureType : DieType
		{
			int byte_size;
			public readonly bool IsUnion;

			public DieStructureType (DwarfBinaryReader reader,
						 CompilationUnit comp_unit, long offset,
						 AbbrevEntry abbrev, bool is_union)
				: base (reader, comp_unit, offset, abbrev)
			{
				this.IsUnion = is_union;
			}

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

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.inheritance:
					return new DieInheritance (reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			ArrayList members;
			NativeFieldInfo[] fields;
			new NativeStructType type;

			protected override NativeType CreateType ()
			{
				type = new NativeStructType (Name, fields, byte_size);
				return type;
			}

			protected override void PopulateType ()
			{
				if (!abbrev.HasChildren)
					return;

				ArrayList list = new ArrayList ();

				foreach (Die child in Children) {
					DieMember member = child as DieMember;
					if ((member == null) || !member.Resolve (this))
						continue;

					NativeType mtype = member.Type;
					if (mtype == null)
						mtype = NativeType.VoidType;

					NativeFieldInfo field;
					if (member.IsBitfield)
						field = new NativeFieldInfo (
							mtype, member.Name, list.Count,
							member.DataOffset, member.BitOffset,
							member.BitSize);
					else
						field = new NativeFieldInfo (
							mtype, member.Name, list.Count,
							member.DataOffset);
					list.Add (field);
				}

				fields = new NativeFieldInfo [list.Count];
				list.CopyTo (fields);

				type.SetFields (fields);
			}
		}

		protected class DieSubroutineType : DieType
		{
			long type_offset;
			bool prototyped;
			DieType return_type;

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

			protected override NativeType CreateType ()
			{
				if (!prototyped)
					return null;

				if (type_offset != 0) {
					return_type = GetReference (type_offset);
					if (return_type == null)
						return null;
				}

				NativeType ret_type = null;
				if (return_type != null)
					ret_type = return_type.ResolveType ();
				if (ret_type == null)
					ret_type = NativeType.VoidType;

				NativeType[] param_types = new NativeType [0];
				NativeFunctionType func_type = new NativeFunctionType (
					"test", ret_type, param_types);
				return func_type;
			}

			protected override void PopulateType ()
			{
				if (!abbrev.HasChildren)
					return;

				ArrayList args = new ArrayList ();

				foreach (Die child in Children) {
					DieFormalParameter formal = child as DieFormalParameter;
					if (formal == null)
						continue;

					args.Add (formal);
				}
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
					switch (attribute.DwarfForm) {
					case DwarfForm.block1:
						location_block = (byte []) attribute.Data;
						use_constant = false;
						break;
					case DwarfForm.data1:
					case DwarfForm.data2:
					case DwarfForm.data4:
					case DwarfForm.data8:
						location_constant = (long) attribute.Data;
						use_constant = true;
						break;
					}
					break;
				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}


			byte[] location_block;
			long location_constant;
			bool use_constant;
			TargetVariable variable;
			ITargetInfo target_info;
			bool resolved;

			protected bool DoResolve ()
			{
				if ((TypeOffset == 0) || (!use_constant && location_block == null) || (Name == null))
					return false;

				DieType reference = comp_unit.GetType (TypeOffset);
				if (reference == null)
					return false;

				NativeType type = reference.ResolveType ();
				if (!use_constant) {
					TargetBinaryReader locreader = new TargetBinaryReader (
						       location_block, target_info);
					variable = new TargetVariable (
						dwarf, Name, type, locreader);
				}
				else {
					variable = new TargetVariable (
						dwarf, Name, type, (int)location_constant);
				}
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

				case DwarfAttribute.bit_offset:
					bit_offset = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.bit_size:
					bit_size = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			byte[] location;
			bool resolved, ok;
			DieType type_die;
			NativeType type;
			ITargetInfo target_info;
			int bit_offset, bit_size;
			int offset;

			public bool Resolve (DieStructureType die_struct)
			{
				if (resolved)
					return ok;

				type = ResolveType (die_struct);
				resolved = true;
				ok = type != null;
				return ok;
			}

			public bool IsBitfield {
				get { return bit_size != 0; }
			}

			public int BitOffset {
				get { return bit_offset; }
			}

			public int BitSize {
				get { return bit_size; }
			}

			bool read_location ()
			{
				TargetBinaryReader locreader = new TargetBinaryReader (
					location, target_info);

				switch (locreader.ReadByte ()) {
				case 0x23: // DW_OP_plus_uconstant
					offset = locreader.ReadLeb128 ();
					return locreader.IsEof;

				default:
					return false;
				}
			}

			protected NativeType ResolveType (DieStructureType die_struct)
			{
				if ((TypeOffset == 0) || (Name == null))
					return null;

				if ((location == null) && !die_struct.IsUnion)
					return null;

				type_die = comp_unit.GetType (TypeOffset);
				if (type_die == null)
					return null;

				if ((location != null) && !read_location ())
					return null;

				type = type_die.ResolveType ();
				return type;
			}

			public NativeType Type {
				get {
					if (!resolved)
						throw new InvalidOperationException ();

					return type;
				}
			}

			public int DataOffset {
				get {
					if (!resolved)
						throw new InvalidOperationException ();

					return offset;
				}
			}
		}
	}
}
