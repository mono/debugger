using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal class DwarfReader : IDisposable
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

		protected class DwarfException : Exception
		{
			public DwarfException (DwarfReader reader, string message)
				: base (String.Format ("{0}: {1}", reader.FileName, message))
			{ }

			public DwarfException (DwarfReader reader, string message, Exception inner)
				: base (String.Format ("{0}: {1}", reader.FileName, message), inner)
			{ }
		}

		public DwarfReader (Bfd bfd, Module module, ISymbolTable simple_symtab)
		{
			this.bfd = bfd;
			this.module = module;
			this.filename = bfd.FileName;

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

			Console.WriteLine ("Reading aranges table ....");

			aranges = ArrayList.Synchronized (read_aranges ());

			symtab = new DwarfSymbolTable (this, aranges, simple_symtab);

			Console.WriteLine ("Done reading aranges table");
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

		protected long find_compile_unit_block (long offset)
		{
			for (int i = aranges.Count-1; i >= 0; i--) {
				RangeEntry entry = (RangeEntry) aranges [i];

				if (entry.FileOffset < offset)
					return entry.FileOffset;
			}

			return -1;
		}

		protected ISymbolTable get_symtab_at_offset (long offset)
		{
			CompileUnitBlock block;
			lock (compile_unit_hash.SyncRoot) {
				block = (CompileUnitBlock) compile_unit_hash [offset];
				if (block == null) {
					block = new CompileUnitBlock (this, offset);
					compile_unit_hash.Add (offset, block);
				}
			}

			// This either return the already-read symbol table or acquire the
			// thread lock and read it.
			return block.SymbolTable;
		}

		public SourceInfo[] GetSources ()
		{
			Hashtable source_hash = new Hashtable ();

			foreach (IMethod method in symtab.GetAllMethods ()) {
				if (!method.HasSource)
					continue;

				ISourceBuffer buffer = method.Source.SourceBuffer;
				if ((buffer == null) || buffer.HasContents)
					continue;

				DwarfSourceInfo source = (DwarfSourceInfo) source_hash [buffer.Name];
				if (source == null) {
					source = new DwarfSourceInfo (this, buffer.Name);
					source_hash.Add (buffer.Name, source);
				}
				source.AddMethod (new DwarfSourceMethodInfo (source, method));
			}

			SourceInfo[] retval = new SourceInfo [source_hash.Values.Count];
			source_hash.Values.CopyTo (retval, 0);
			return retval;
		}

		private class DwarfSourceMethodInfo : SourceMethodInfo
		{
			IMethod method;

			public DwarfSourceMethodInfo (SourceInfo source, IMethod method)
				: base (source, method.Name, method.Source.StartRow, method.Source.EndRow,
					false)
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

		private class DwarfSourceInfo : SourceInfo
		{
			DwarfReader dwarf;
			ArrayList methods;

			public DwarfSourceInfo (DwarfReader dwarf, string filename)
				: base (dwarf.module, filename)
			{
				this.dwarf = dwarf;
				this.methods = new ArrayList ();
			}

			public void AddMethod (DwarfSourceMethodInfo method)
			{
				methods.Add (method);
			}

			protected override ArrayList GetMethods ()
			{
				return methods;
			}
		}

		protected class CompileUnitBlock
		{
			public readonly DwarfReader dwarf;
			public readonly long offset;

			SymbolTableCollection symtabs;
			ArrayList compile_units;
			bool initialized;

			public IMethod Lookup (TargetAddress address)
			{
				read_block ();
				foreach (CompilationUnit comp_unit in compile_units) {
					ISymbolTable symtab = comp_unit.SymbolTable;

					IMethod method = symtab.Lookup (address);
					if (method != null)
						return method;
				}

				return null;
			}

			public ISymbolTable SymbolTable {
				get {
					read_block ();
					return symtabs;
				}
			}

			void read_block ()
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

					DwarfBinaryReader reader = dwarf.DebugInfoReader;
					reader.Position = offset;
					long length = reader.ReadInitialLength ();
					long stop = reader.Position + length;
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

					compile_units = new ArrayList ();

					while (reader.Position < stop) {
						CompilationUnit comp_unit = new CompilationUnit (dwarf, reader);
						compile_units.Add (comp_unit);
						symtabs.AddSymbolTable (comp_unit.SymbolTable);
					}

					symtabs.UnLock ();

					initialized = true;
				}
			}

			public CompileUnitBlock (DwarfReader dwarf, long start)
			{
				this.dwarf = dwarf;
				this.offset = start;
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
			ISymbolTable simple_symtab;

			public DwarfSymbolTable (DwarfReader dwarf, ArrayList ranges, ISymbolTable simple)
			{
				this.dwarf = dwarf;
				this.simple_symtab = simple;
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

			public override string SimpleLookup (TargetAddress address, bool exact_match)
			{
				string name = base.SimpleLookup (address, exact_match);
				if (name != null)
					return name;

				return simple_symtab.SimpleLookup (address, exact_match);
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

#if FALSE
		private class PubNameEntry : ISymbol
		{
			public readonly long FileOffset;
			string name;
			DwarfReader dwarf;

			public PubNameEntry (DwarfReader dwarf, long offset, string name)
			{
				this.dwarf = dwarf;
				this.FileOffset = dwarf.find_compile_unit_block (offset);
				this.name = name;
			}

			public string Name {
				get {
					return name;
				}
			}

			public ITargetLocation Location {
				get {
					ISourceLookup symtab = dwarf.get_symtab_at_offset (FileOffset);
					if (symtab == null)
						return null;

					ISymbol symbol = symtab.Lookup (Name);
					if (symbol == null)
						return null;

					return symbol.Location;
				}
			}

			public int CompareTo (object obj)
			{
				PubNameEntry entry = (PubNameEntry) obj;

				return name.CompareTo (entry.name);
			}

			public override string ToString ()
			{
				return String.Format ("DwarfSymbol ({0},{1:x})", Name, FileOffset);
			}
		}
#endif

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

#if FALSE
		ArrayList read_pubnames ()
		{
			DwarfBinaryReader reader = DebugPubnamesReader;

			ArrayList pubnames = new ArrayList ();

			while (!reader.IsEof) {
				long start = reader.Position;
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long offset = reader.ReadOffset ();
				long section_length = reader.ReadOffset ();

				if (version != 2)
					throw new DwarfException (this, String.Format (
						"Wrong version in .debug_pubnames: {0}", version));

				while (reader.Position < stop) {
					long section_offset = reader.ReadOffset ();
					if (section_offset == 0)
						break;

					string name = reader.ReadString ();

					pubnames.Add (new PubNameEntry (this, section_offset, name));
				}
			}

			return pubnames;
		}
#endif

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

		protected class DwarfNativeMethod : MethodBase
		{
			protected LineNumberEngine engine;
			protected DieSubprogram subprog;

			public DwarfNativeMethod (DieSubprogram subprog, LineNumberEngine engine)
				: base (subprog.Name, subprog.ImageFile, subprog.dwarf.module)
			{
				this.subprog = subprog;
				this.engine = engine;

				if (subprog.IsContinuous)
					SetAddresses (subprog.StartAddress, subprog.EndAddress);

				if (engine != null)
					SetSource (new DwarfNativeMethodSource (this));
			}

			public override object MethodHandle {
				get {
					return this;
				}
			}

			public override IVariable[] Parameters {
				get {
					throw new NotSupportedException ();
				}
			}

			public override IVariable[] Locals {
				get {
					throw new NotSupportedException ();
				}
			}

			private class DwarfNativeMethodSource : MethodSource
			{
				DwarfNativeMethod method;

				public DwarfNativeMethodSource (DwarfNativeMethod method)
					: base (method)
				{
					this.method = method;
				}

				protected override ISourceBuffer ReadSource (
					out int start_row, out int end_row, out ArrayList addresses)
				{
					start_row = end_row = 0;
					addresses = null;

					if (method.engine == null)
						throw new InternalError ();

					string file = method.engine.GetSource (
						method.subprog, out start_row, out end_row, out addresses);
					if (file == null)
						return null;

					if ((addresses != null) && (addresses.Count > 2)) {
						LineEntry start = (LineEntry) addresses [1];
						LineEntry end = (LineEntry) addresses [addresses.Count - 1];

						method.SetMethodBounds (start.Address, end.Address);
					}

					return new SourceBuffer (file);
				}
			}
		}

		protected class DwarfCompileUnitSymbolTable : SymbolTable
		{
			DieCompileUnit comp_unit_die;

			public DwarfCompileUnitSymbolTable (DieCompileUnit comp_unit_die)
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

				ArrayList list = new ArrayList ();

				lock (comp_unit_die) {
					foreach (Die child in comp_unit_die.Children) {
						DieSubprogram subprog = child as DieSubprogram;
						if ((subprog == null) || !subprog.IsContinuous)
							continue;

						list.Add (subprog);
					}
				}

				list.Sort ();

				LineNumberEngine engine = null;
				if (comp_unit_die.LineNumberOffset >= 0)
					engine = new LineNumberEngine (
						comp_unit_die.dwarf, comp_unit_die.LineNumberOffset,
						comp_unit_die.CompilationDirectory, list);

				foreach (DieSubprogram subprog in list) {
					DwarfNativeMethod native = new DwarfNativeMethod (subprog, engine);

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
			comp_dir		= 0x1b,
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

			ISymbolContainer next_method, current_method;
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
					this.st_address = 0;
					this.st_file = 0;
					this.st_line = 1;
					this.st_column = 0;
					this.is_stmt = this.engine.default_is_stmt;
					this.basic_block = false;
					this.end_sequence = false;
					this.prologue_end = false;
					this.epilogue_begin = false;

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

					TargetAddress address = engine.dwarf.GetAddress (st_address);
					lines.Add (new LineEntry (address, st_line));

					basic_block = false;
					prologue_end = false;
					epilogue_begin = false;
				}

				void set_end_sequence ()
				{
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
							"Unknown extended opcode {0:x} in line number engine",
							opcode));
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
					next_method_address = next_method.StartAddress.Address;
				} else
					next_method_address = Int64.MaxValue;

				debug ("NEW NEXT: {0:x}", next_method_address);
			}

			void commit (StatementMachine stm)
			{
				debug ("COMMIT: {0:x} {1}", stm.st_address, stm.st_line);

				if (stm.st_address >= next_method_address)
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

				// Console.WriteLine (this);

				next_method_index = 1;
				next_method = (ISymbolContainer) methods [0];
				next_method_address = next_method.StartAddress.Address;

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

			ObjectCache children_cache;

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

			object read_children (object user_data)
			{
				DwarfBinaryReader reader = dwarf.DebugInfoReader;

				long old_pos = reader.Position;
				reader.Position = ChildrenOffset;
				ArrayList children = ReadChildren (reader);
				reader.Position = old_pos;

				return children;
			}

			public ArrayList Children {
				get {
					if (!abbrev.HasChildren)
						return null;

					if (children_cache == null)
						children_cache = new ObjectCache
							(new ObjectCacheFunc (read_children), null, 1);

					return (ArrayList) children_cache.Data;
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
			string comp_dir;
			bool is_continuous;

			protected long line_offset;
			protected bool has_lines;

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

					case DwarfAttribute.comp_dir:
						comp_dir = (string) attribute.Data;
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
		}

		protected class DieSubprogram : Die, IComparable, ISymbolContainer
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

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2:x}:{3:x})", GetType (),
						      Name, start_pc, end_pc);
			}
		}

		protected class CompilationUnit
		{
			DwarfReader dwarf;
			long start_offset, unit_length, abbrev_offset;
			int version, address_size;
			DieCompileUnit comp_unit_die;
			DwarfCompileUnitSymbolTable symtab;
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

				// Console.WriteLine (this);

				abbrevs = new Hashtable ();

				DwarfBinaryReader abbrev_reader = dwarf.DebugAbbrevReader;

				abbrev_reader.Position = abbrev_offset;
				while (abbrev_reader.PeekByte () != 0) {
					AbbrevEntry entry = new AbbrevEntry (dwarf, abbrev_reader);
					abbrevs.Add (entry.ID, entry);
				}

				comp_unit_die = new DieCompileUnit (reader, this);

				symtab = new DwarfCompileUnitSymbolTable (comp_unit_die);

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
