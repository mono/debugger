using System;
using System.IO;
using System.Text;
using System.Collections;
using Mono.CSharp.Debugger;
using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	public class SymbolTableException : Exception
	{
		public SymbolTableException ()
			: base ("Invalid mono symbol table")
		{ }

		public SymbolTableException (string message)
			: base (message)
		{ }
	}

	public class SymbolTableEmptyException : SymbolTableException
	{
		public SymbolTableEmptyException ()
			: base ("Empty symbol table")
		{ }
	}

	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress trampoline_code;
		public readonly TargetAddress symbol_file_generation;
		public readonly TargetAddress symbol_file_table;
		public readonly TargetAddress update_symbol_file_table;
		public readonly TargetAddress compile_method;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			reader.Offset = reader.TargetLongIntegerSize +
				2 * reader.TargetIntegerSize;
			trampoline_code = reader.ReadAddress ();
			symbol_file_generation = reader.ReadAddress ();
			symbol_file_table = reader.ReadAddress ();
			update_symbol_file_table = reader.ReadAddress ();
			compile_method = reader.ReadAddress ();
		}

		public override string ToString ()
		{
			return String.Format ("MonoDebuggerInfo ({0:x}, {1:x}, {2:x}, {3:x}, {4:x})",
					      trampoline_code, symbol_file_generation, symbol_file_table,
					      update_symbol_file_table, compile_method);
		}
	}

	internal class MonoSymbolTableReader
	{
		MethodEntry[] Methods;
		protected string ImageFile;
		protected OffsetTable offset_table;
		protected ILanguageBackend language;
		protected IInferior inferior;
		ArrayList ranges;

		int is_dynamic;

		TargetAddress raw_contents;
		int raw_contents_size;

		TargetAddress address_table;
		int address_table_size;

		TargetAddress range_table;
		int range_table_size;

		TargetAddress string_table;
		int string_table_size;

		TargetBinaryReader reader;
		TargetBinaryReader address_reader;
		ITargetMemoryReader range_reader;
		ITargetMemoryReader string_reader;

		internal MonoSymbolTableReader (IInferior inferior,
						ITargetMemoryReader symtab_reader,
						ILanguageBackend language)
		{
			this.inferior = inferior;
			this.language = language;

			if (symtab_reader.ReadLongInteger () != OffsetTable.Magic)
				throw new SymbolTableException ();

			if (symtab_reader.ReadInteger () != OffsetTable.Version)
				throw new SymbolTableException ();

			is_dynamic = symtab_reader.ReadInteger ();
			TargetAddress image_file_addr = symtab_reader.ReadAddress ();
			ImageFile = inferior.ReadString (image_file_addr);
			raw_contents = symtab_reader.ReadAddress ();
			raw_contents_size = symtab_reader.ReadInteger ();
			address_table = symtab_reader.ReadAddress ();
			address_table_size = symtab_reader.ReadInteger ();
			range_table = symtab_reader.ReadAddress ();
			range_table_size = symtab_reader.ReadInteger ();
			string_table = symtab_reader.ReadAddress ();
			string_table_size = symtab_reader.ReadInteger ();
			symtab_reader.ReadAddress ();

			if ((raw_contents_size == 0) || (address_table_size == 0))
				throw new SymbolTableEmptyException ();

			reader = inferior.ReadMemory (raw_contents, raw_contents_size).BinaryReader;
			address_reader = inferior.ReadMemory (address_table, address_table_size).BinaryReader;
			range_reader = inferior.ReadMemory (range_table, range_table_size);
			string_reader = inferior.ReadMemory (string_table, string_table_size);

			//
			// Read the offset table.
			//
			try {
				long magic = reader.ReadInt64 ();
				int version = reader.ReadInt32 ();
				if ((magic != OffsetTable.Magic) || (version != OffsetTable.Version))
					throw new SymbolTableException ();
				offset_table = new OffsetTable (reader);
			} catch {
				throw new SymbolTableException ();
			}

			ranges = MethodRangeEntry.ReadRanges (
				this, range_reader, range_table_size, offset_table);
		}

		internal int CheckMethodOffset (long offset)
		{
			if (offset < offset_table.method_table_offset)
				throw new SymbolTableException ();

			offset -= offset_table.method_table_offset;
			if ((offset % MethodEntry.Size) != 0)
				throw new SymbolTableException ();

			long index = (offset / MethodEntry.Size);
			if (index > offset_table.method_count)
				throw new SymbolTableException ();

			return (int) index;
		} 

		internal ISymbolLookup GetMethod (long offset)
		{
			int index = CheckMethodOffset (offset);
			reader.Position = offset;
			MethodEntry method = new MethodEntry (reader, address_reader);
			string_reader.Offset = index * inferior.TargetIntegerSize;
			string_reader.Offset = string_reader.ReadInteger ();

			StringBuilder sb = new StringBuilder ();
			while (true) {
				byte b = string_reader.ReadByte ();
				if (b == 0)
					break;

				sb.Append ((char) b);
			}

			return new MonoMethod (this, method, sb.ToString ());
		}

		internal ArrayList SymbolRanges {
			get {
				return ranges;
			}
		}

		private class MonoMethod : MethodBase
		{
			MonoSymbolTableReader reader;
			MethodEntry method;
			ISourceFileFactory factory;

			public MonoMethod (MonoSymbolTableReader reader, MethodEntry method,
					   string name)
				: base (name, reader.ImageFile, method.Token >> 24 == 6)
			{
				this.reader = reader;
				this.method = method;
				this.factory = new SourceFileFactory ();

				if (method.Address != null) {
					TargetAddress start = new TargetAddress (
						reader.inferior, method.Address.StartAddress);
					TargetAddress end = new TargetAddress (
						reader.inferior, method.Address.EndAddress);
					SetAddresses (start, end);
				}
			}

			protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
								     out ArrayList addresses)
			{
				ISourceBuffer retval = CSharpMethod.GetMethodSource (
					this, method, factory, out start_row, out end_row, out addresses);

				if ((addresses != null) && (addresses.Count > 2)) {
					LineEntry start = (LineEntry) addresses [1];
					LineEntry end = (LineEntry) addresses [addresses.Count - 1];

					SetMethodBounds (start.Address, end.Address);
				}

				return retval;
			}

			public override ILanguageBackend Language {
				get {
					return reader.language;
				}
			}

			public override object MethodHandle {
				get {
					return method.Token;
				}
			}
		}

		private class MethodRangeEntry : SymbolRangeEntry
		{
			MonoSymbolTableReader reader;
			long file_offset;

			private MethodRangeEntry (MonoSymbolTableReader reader, long offset,
						  TargetAddress start_address, TargetAddress end_address)
				: base (start_address, end_address)
			{
				this.reader = reader;
				this.file_offset = offset;
			}

			public static ArrayList ReadRanges (MonoSymbolTableReader reader,
							    ITargetMemoryReader memory, long size,
							    OffsetTable offset_table)
			{
				ArrayList list = new ArrayList ();

				while (memory.Offset < size) {
					long start = memory.ReadLongInteger ();
					long end = memory.ReadLongInteger ();
					int offset = memory.ReadInteger ();

					reader.CheckMethodOffset (offset);

					TargetAddress tstart = new TargetAddress (reader.inferior, start);
					TargetAddress tend = new TargetAddress (reader.inferior, end);

					list.Add (new MethodRangeEntry (reader, offset, tstart, tend));
				}

				return list;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return reader.GetMethod (file_offset);
			}
		}
	}

	internal class MonoCSharpLanguageBackend : SymbolTable, ILanguageBackend
	{
		IInferior inferior;
		MonoDebuggerInfo info;
		int symtab_generation;
		ArrayList symtabs;
		ArrayList ranges;
		TargetAddress trampoline_address;
		IArchitecture arch;

		public MonoCSharpLanguageBackend (IInferior inferior)
		{
			this.inferior = inferior;
		}

		public ISymbolTable SymbolTable {
			get {
				return this;
			}
		}

		public override bool HasRanges {
			get {
				return ranges != null;
			}
		}

		public override ISymbolRange[] SymbolRanges {
			get {
				if (ranges == null)
					throw new InvalidOperationException ();
				ISymbolRange[] retval = new ISymbolRange [ranges.Count];
				ranges.CopyTo (retval, 0);
				return retval;
			}
		}

		protected override bool HasMethods {
			get {
				return false;
			}
		}

		protected override ArrayList GetMethods ()
		{
			throw new InvalidOperationException ();
		}

		void read_mono_debugger_info ()
		{
			if (info != null)
				return;

			TargetAddress symbol_info = inferior.SimpleLookup ("MONO_DEBUGGER__debugger_info");
			if (symbol_info.IsNull)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			ITargetMemoryReader header = inferior.ReadMemory (symbol_info, 16);
			if (header.ReadLongInteger () != OffsetTable.Magic)
				throw new SymbolTableException ();
			if (header.ReadInteger () != OffsetTable.Version)
				throw new SymbolTableException ();

			int size = (int) header.ReadInteger ();

			ITargetMemoryReader table = inferior.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			trampoline_address = inferior.ReadAddress (info.trampoline_code);
			arch = inferior.Architecture;
		}

		bool updating_symfiles;
		public override void UpdateSymbolTable ()
		{
			if (updating_symfiles)
				return;

			read_mono_debugger_info ();

			try {
				int generation = inferior.ReadInteger (info.symbol_file_generation);
				if (generation == symtab_generation)
					return;
			} catch {
				return;
			}

			try {
				updating_symfiles = true;

				int result = (int) inferior.CallMethod (info.update_symbol_file_table, 0);

				// Nothing to do.
				if (result == 0)
					return;

				do_update_symbol_files ();
			} catch {
				symtabs = null;
				ranges = null;
			} finally {
				updating_symfiles = false;
				base.UpdateSymbolTable ();
			}
		}

		void do_update_symbol_files ()
		{
			Console.WriteLine ("Updating symbol files.");

			symtabs = new ArrayList ();
			ranges = new ArrayList ();

			int header_size = 3 * inferior.TargetIntegerSize;
			TargetAddress symbol_file_table = inferior.ReadAddress (info.symbol_file_table);
			ITargetMemoryReader header = inferior.ReadMemory (symbol_file_table, header_size);

			int size = header.ReadInteger ();
			int count = header.ReadInteger ();
			symtab_generation = header.ReadInteger ();

			ITargetMemoryReader symtab_reader = inferior.ReadMemory (
				symbol_file_table, size + header_size);
			symtab_reader.Offset = header_size;

			for (int i = 0; i < count; i++) {
				MonoSymbolTableReader reader;
				try {
					reader = new MonoSymbolTableReader (inferior, symtab_reader, this);
					ranges.AddRange (reader.SymbolRanges);
					symtabs.Add (reader);
				} catch (SymbolTableEmptyException) {
					continue;
				} catch (Exception) {
					throw new SymbolTableException ();
				}
			}

			ranges.Sort ();

			Console.WriteLine ("Done updating symbol files.");
		}

		public TargetAddress GenericTrampolineCode {
			get {
				return trampoline_address;
			}
		}

		public TargetAddress GetTrampoline (TargetAddress address)
		{
			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			TargetAddress trampoline = arch.GetTrampoline (address, trampoline_address);

			if (trampoline.IsNull)
				return TargetAddress.Null;

			long result = inferior.CallMethod (info.compile_method, trampoline.Address);

			TargetAddress method;
			switch (inferior.TargetAddressSize) {
			case 4:
				method = new TargetAddress (inferior, (int) result);
				break;

			case 8:
				method = new TargetAddress (inferior, result);
				break;
				
			default:
				throw new TargetMemoryException (
					"Unknown target address size " + inferior.TargetAddressSize);
			}

			return method;
		}
	}
}
