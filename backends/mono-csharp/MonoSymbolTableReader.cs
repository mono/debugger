using System;
using System.IO;
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
		public readonly ITargetLocation trampoline_code;
		public readonly ITargetLocation symbol_file_generation;
		public readonly ITargetLocation symbol_file_table;
		public readonly ITargetLocation update_symbol_file_table;
		public readonly ITargetLocation compile_method;

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
		ArrayList ranges;

		int is_dynamic;

		ITargetLocation raw_contents;
		int raw_contents_size;

		ITargetLocation address_table;
		int address_table_size;

		ITargetLocation range_table;
		int range_table_size;

		BinaryReader reader;
		BinaryReader address_reader;
		ITargetMemoryReader range_reader;

		internal MonoSymbolTableReader (ITargetMemoryAccess memory,
						ITargetMemoryReader symtab_reader)
		{
			if (symtab_reader.ReadLongInteger () != OffsetTable.Magic)
				throw new SymbolTableException ();

			if (symtab_reader.ReadInteger () != OffsetTable.Version)
				throw new SymbolTableException ();

			is_dynamic = symtab_reader.ReadInteger ();
			ITargetLocation image_file_addr = symtab_reader.ReadAddress ();
			ImageFile = memory.ReadString (image_file_addr);
			raw_contents = symtab_reader.ReadAddress ();
			raw_contents_size = symtab_reader.ReadInteger ();
			address_table = symtab_reader.ReadAddress ();
			address_table_size = symtab_reader.ReadInteger ();
			range_table = symtab_reader.ReadAddress ();
			range_table_size = symtab_reader.ReadInteger ();
			symtab_reader.ReadAddress ();

			if ((raw_contents_size == 0) || (address_table_size == 0))
				throw new SymbolTableEmptyException ();

			reader = memory.ReadMemory (raw_contents, raw_contents_size).BinaryReader;
			address_reader = memory.ReadMemory (address_table, address_table_size).BinaryReader;
			range_reader = memory.ReadMemory (range_table, range_table_size);

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

#if FALSE
			//
			// Read the method table.
			//
			binreader.BaseStream.Position = offset_table.method_table_offset;

			Methods = new MethodEntry [offset_table.method_count];

			for (int i = 0; i < offset_table.method_count; i++) {
				try {
					Methods [i] = new MethodEntry (binreader, address_reader.BinaryReader);
				} catch {
					throw new SymbolTableException ("Can't read method table");
				}
			}
#endif
		}

		internal void CheckMethodOffset (long offset)
		{
			if (offset < offset_table.method_table_offset)
				throw new SymbolTableException ();

			offset -= offset_table.method_table_offset;
			if ((offset % MethodEntry.Size) != 0)
				throw new SymbolTableException ();
			if ((offset / MethodEntry.Size) > offset_table.method_count)
				throw new SymbolTableException ();
		} 

		internal ISymbolLookup GetMethod (long offset)
		{
			reader.BaseStream.Position = offset;
			MethodEntry method = new MethodEntry (reader, address_reader);
			return new MonoMethod (this, method);
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

			public MonoMethod (MonoSymbolTableReader reader, MethodEntry method)
				: base (String.Format ("C#({0:x})", method.Token), reader.ImageFile,
					method.Token >> 24 == 6)
			{
				this.reader = reader;
				this.method = method;
				this.factory = new SourceFileFactory ();

				if (method.Address != null)
					SetAddresses (method.Address.StartAddress, method.Address.EndAddress);
			}

			protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
								     out ArrayList addresses)
			{
				return CSharpMethod.GetMethodSource (
					this, method, factory, out start_row, out end_row, out addresses);
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

			private MethodRangeEntry (MonoSymbolTableReader reader,
						  long offset, long start_address, long end_address)
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

					list.Add (new MethodRangeEntry (reader, offset, start, end));
				}

				return list;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return reader.GetMethod (file_offset);
			}
		}
	}

	internal class MonoSymbolTableCollection : SymbolTable
	{
		IInferior inferior;
		ITargetMemoryAccess memory;
		MonoDebuggerInfo info;
		int symtab_generation;
		ArrayList symtabs;
		ArrayList ranges;

		public MonoSymbolTableCollection (IInferior inferior)
		{
			this.inferior = inferior;
			this.memory = inferior;
		}

		public override bool HasRanges {
			get {
				return true;
			}
		}

		public override ISymbolRange[] SymbolRanges {
			get {
				if (ranges == null)
					return null;
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

			ITargetLocation symbol_info = inferior.SimpleLookup ("MONO_DEBUGGER__debugger_info");
			if (symbol_info == null)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			ITargetMemoryReader header = memory.ReadMemory (symbol_info, 16);
			if (header.ReadLongInteger () != OffsetTable.Magic)
				throw new SymbolTableException ();
			if (header.ReadInteger () != OffsetTable.Version)
				throw new SymbolTableException ();

			int size = (int) header.ReadInteger ();

			ITargetMemoryReader table = memory.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			// arch.GenericTrampolineCode = inferior.ReadAddress (mono_debugger_info.trampoline_code);
		}

		bool updating_symfiles;
		public void UpdateSymbolTables ()
		{
			if (updating_symfiles)
				return;

			read_mono_debugger_info ();

			try {
				int generation = memory.ReadInteger (info.symbol_file_generation);
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
			}
		}

		void do_update_symbol_files ()
		{
			Console.WriteLine ("Updating symbol files.");

			symtabs = new ArrayList ();
			ranges = new ArrayList ();

			int header_size = 3 * memory.TargetIntegerSize;
			ITargetLocation symbol_file_table = memory.ReadAddress (info.symbol_file_table);
			ITargetMemoryReader header = memory.ReadMemory (symbol_file_table, header_size);

			int size = header.ReadInteger ();
			int count = header.ReadInteger ();
			symtab_generation = header.ReadInteger ();

			ITargetMemoryReader symtab_reader = memory.ReadMemory (
				symbol_file_table, size + header_size);
			symtab_reader.Offset = header_size;

			for (int i = 0; i < count; i++) {
				MonoSymbolTableReader reader;
				try {
					reader = new MonoSymbolTableReader (memory, symtab_reader);
					ranges.AddRange (reader.SymbolRanges);
					symtabs.Add (reader);
				} catch (SymbolTableEmptyException e) {
					continue;
				} catch (Exception e) {
					throw new SymbolTableException ();
				}
			}

			ranges.Sort ();

			Console.WriteLine ("Done updating symbol files.");
		}
	}
}
