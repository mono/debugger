using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using Mono.CSharp.Debugger;
using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class VariableInfo
	{
		public readonly int Index;
		public readonly int Offset;
		public readonly int Size;
		public readonly AddressMode Mode;
		public readonly int BeginScope;
		public readonly int EndScope;

		public enum AddressMode : long
		{
			Stack		= 0,
			Register	= 0x10000000,
			TwoRegisters	= 0x20000000
		}

		const long AddressModeFlags = 0xf0000000;

		public static int StructSize {
			get {
				return 20;
			}
		}

		public VariableInfo (TargetBinaryReader reader)
		{
			Index = reader.ReadInt32 ();
			Offset = reader.ReadInt32 ();
			Size = reader.ReadInt32 ();
			BeginScope = reader.ReadInt32 ();
			EndScope = reader.ReadInt32 ();

			Mode = (AddressMode) (Index & AddressModeFlags);
			Index = (int) ((long) Index & ~AddressModeFlags);
		}

		public override string ToString ()
		{
			return String.Format ("[VariableInfo {0}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}]",
					      Mode, Index, Offset, Size, BeginScope, EndScope);
		}
	}

	internal class MethodAddress
	{
		public readonly long StartAddress;
		public readonly long EndAddress;
		public readonly int[] LineAddresses;
		public readonly VariableInfo ThisVariableInfo;
		public readonly VariableInfo[] ParamVariableInfo;
		public readonly VariableInfo[] LocalVariableInfo;
		public readonly long ThisTypeInfoAddress;
		public readonly long[] ParamTypeInfoAddresses;
		public readonly long[] LocalTypeInfoAddresses;

		public MethodAddress (MethodEntry entry, TargetBinaryReader reader)
		{
			reader.Position = 4;
			StartAddress = reader.ReadInt64 ();
			EndAddress = reader.ReadInt64 ();
			LineAddresses = new int [entry.NumLineNumbers];

			int lines_offset = reader.ReadInt32 ();
			int variables_offset = reader.ReadInt32 ();
			int type_table_offset = reader.ReadInt32 ();

			reader.Position = lines_offset;
			for (int i = 0; i < entry.NumLineNumbers; i++)
				LineAddresses [i] = reader.ReadInt32 ();

			reader.Position = variables_offset;
			if (entry.ThisTypeIndex != 0)
				ThisVariableInfo = new VariableInfo (reader);

			ParamVariableInfo = new VariableInfo [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamVariableInfo [i] = new VariableInfo (reader);

			LocalVariableInfo = new VariableInfo [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalVariableInfo [i] = new VariableInfo (reader);

			reader.Position = type_table_offset;
			if (entry.ThisTypeIndex != 0)
				ThisTypeInfoAddress = reader.ReadAddress ();

			ParamTypeInfoAddresses = new long [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamTypeInfoAddresses [i] = reader.ReadAddress ();

			LocalTypeInfoAddresses = new long [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalTypeInfoAddresses [i] = reader.ReadAddress ();
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{2}]",
					      StartAddress, EndAddress, LineAddresses.Length);
		}
	}

	internal class MonoSymbolFileTable
	{
		public const int  DynamicVersion = 7;
		public const long DynamicMagic   = 0x7aff65af4253d427;

		public readonly int TotalSize;
		public readonly int Generation;
		public readonly MonoSymbolTableReader[] SymbolFiles;
		ITargetMemoryAccess memory;
		ArrayList ranges;
		Hashtable types;
		Hashtable type_cache;

		public MonoSymbolFileTable (DebuggerBackend backend, IInferior inferior,
					    ITargetMemoryAccess memory, TargetAddress address,
					    ILanguageBackend language)
		{
			this.memory = memory;

			ITargetMemoryReader header = memory.ReadMemory (address, 24);

			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"Dynamic section has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version != DynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but expected {1}.",
					version, DynamicVersion);

			TotalSize = header.ReadInteger ();
			int count = header.ReadInteger ();
			Generation = header.ReadInteger ();

			ITargetMemoryReader reader = memory.ReadMemory (address + 24, TotalSize - 24);

			ranges = new ArrayList ();
			types = new Hashtable ();
			type_cache = new Hashtable ();

			SymbolFiles = new MonoSymbolTableReader [count];
			for (int i = 0; i < count; i++)
				SymbolFiles [i] = new MonoSymbolTableReader (
					this, backend, inferior, memory, reader.ReadAddress (), language);
		}

		public MonoType GetType (Type type, int type_size, long address)
		{
			if (type_cache.Contains (address))
				return (MonoType) type_cache [address];

			MonoType retval;
			if (address != 0)
				retval = MonoType.GetType (
					type, memory, new TargetAddress (memory, address), this);
			else
				retval = new MonoOpaqueType (type, type_size);

			type_cache.Add (address, retval);
			return retval;
		}

		public MonoType GetTypeFromClass (long klass_address)
		{
			TypeEntry entry = (TypeEntry) types [klass_address];

			return MonoType.GetType (entry.Type, memory, entry.TypeInfo, this);
		}

		public ArrayList SymbolRanges {
			get {
				return ranges;
			}
		}

		internal void AddType (TypeEntry type)
		{
			types.Add (type.KlassAddress.Address, type);
		}

		public bool Update ()
		{
			bool updated = false;
			for (int i = 0; i < SymbolFiles.Length; i++)
				if (SymbolFiles [i].Update ())
					updated = true;

			if (!updated)
				return false;

			ranges = new ArrayList ();
			for (int i = 0; i < SymbolFiles.Length; i++)
				ranges.AddRange (SymbolFiles [i].SymbolRanges);
			ranges.Sort ();

			return true;
		}
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

	internal class TypeEntry
	{
		public readonly TargetAddress KlassAddress;
		public readonly int Rank;
		public readonly int Token;
		public readonly TargetAddress TypeInfo;
		public readonly Type Type;

		static MethodInfo get_type;

		static TypeEntry ()
		{
			Type type = typeof (Assembly);
			get_type = type.GetMethod ("MonoDebugger_GetType");
			if (get_type == null)
				throw new InternalError (
					"Can't find Assembly.MonoDebugger_GetType");
		}

		private TypeEntry (MonoSymbolTableReader reader, ITargetMemoryReader memory)
		{
			KlassAddress = memory.ReadAddress ();
			Rank = memory.BinaryReader.ReadInt32 ();
			Token = memory.BinaryReader.ReadInt32 ();
			TypeInfo = memory.ReadAddress ();

			object[] args = new object[] { (int) Token };
			Type = (Type) get_type.Invoke (reader.assembly, args);

			if (Type == null)
				throw new InvalidOperationException ();
		}

		public static void ReadTypes (MonoSymbolTableReader reader,
					      ITargetMemoryReader memory, int count)
		{
			for (int i = 0; i < count; i++) {
				try {
					TypeEntry entry = new TypeEntry (reader, memory);
					reader.table.AddType (entry);
				} catch {
					// Do nothing.
				}
			}
		}

		public override string ToString ()
		{
			return String.Format ("TypeEntry [{0:x}:{1:x}:{2:x}]",
					      KlassAddress, Token, TypeInfo);
		}
	}

	internal class MonoSymbolTableReader
	{
		MethodEntry[] Methods;
		internal readonly Assembly assembly;
		internal readonly MonoSymbolFileTable table;
		protected string ImageFile;
		protected string SymbolFile;
		protected OffsetTable offset_table;
		protected ILanguageBackend language;
		protected DebuggerBackend backend;
		protected IInferior inferior;
		protected ITargetMemoryAccess memory;
		ArrayList ranges;

		TargetAddress start_address;
		TargetAddress dynamic_address;
		int address_size;
		int long_size;
		int int_size;

		TargetAddress raw_contents;
		int raw_contents_size;

		int generation;
		int num_range_entries;
		int num_type_entries;

		TargetAddress string_table;
		int string_table_size;

		TargetBinaryReader reader;
		ITargetMemoryReader string_reader;

		internal MonoSymbolTableReader (MonoSymbolFileTable table, DebuggerBackend backend,
						IInferior inferior, ITargetMemoryAccess memory,
						TargetAddress address, ILanguageBackend language)
		{
			this.table = table;
			this.backend = backend;
			this.inferior = inferior;
			this.memory = memory;
			this.language = language;

			start_address = address;
			address_size = memory.TargetAddressSize;
			long_size = memory.TargetLongIntegerSize;
			int_size = memory.TargetIntegerSize;

			ranges = new ArrayList ();

			long magic = memory.ReadLongInteger (address);
			if (magic != OffsetTable.Magic)
				throw new SymbolTableException (
					"Symbol file has unknown magic {0:x}.", magic);
			address += long_size;

			int version = memory.ReadInteger (address);
			if (version != OffsetTable.Version)
				throw new SymbolTableException (
					"Symbol file has version {0}, but expected {1}.",
					version, OffsetTable.Version);
			address += int_size;

			long dynamic_magic = memory.ReadLongInteger (address);
			if (dynamic_magic != MonoSymbolFileTable.DynamicMagic)
				throw new SymbolTableException (
					"Dynamic section has unknown magic {0:x}.", dynamic_magic);
			address += long_size;

			int dynamic_version = memory.ReadInteger (address);
			if (dynamic_version != MonoSymbolFileTable.DynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but expected {1}.",
					dynamic_version, MonoSymbolFileTable.DynamicVersion);
			address += 2 * int_size;

			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);
			TargetAddress symbol_file_addr = memory.ReadAddress (address);
			address += address_size;
			SymbolFile = memory.ReadString (symbol_file_addr);
			raw_contents = memory.ReadAddress (address);
			address += address_size;
			raw_contents_size = memory.ReadInteger (address);
			address += int_size;
			string_table = memory.ReadAddress (address);
			address += address_size;
			string_table_size = memory.ReadInteger (address);
			address += int_size;

			dynamic_address = address;

			assembly = Assembly.LoadFrom (ImageFile);

			if (raw_contents_size == 0)
				throw new SymbolTableException ("Symbol table is empty.");

			if (inferior.State == TargetState.CORE_FILE) {
				// This is a mmap()ed area and thus not written to the core file,
				// so we need to suck the whole file in.
				using (FileStream stream = File.OpenRead (SymbolFile)) {
					byte[] contents = new byte [raw_contents_size];
					stream.Read (contents, 0, raw_contents_size);
					reader = new TargetBinaryReader (contents, inferior);
				}
			} else
				reader = memory.ReadMemory (raw_contents, raw_contents_size).BinaryReader;

			string_reader = memory.ReadMemory (string_table, string_table_size);

			//
			// Read the offset table.
			//
			try {
				magic = reader.ReadInt64 ();
				version = reader.ReadInt32 ();
				if ((magic != OffsetTable.Magic) || (version != OffsetTable.Version))
					throw new SymbolTableException ();
				offset_table = new OffsetTable (reader);
			} catch {
				throw new SymbolTableException ();
			}
		}

		bool update_ranges (ref TargetAddress address)
		{
			TargetAddress range_table = memory.ReadAddress (address);
			address += address_size;
			int range_entry_size = memory.ReadInteger (address);
			address += int_size;
			int new_num_range_entries = memory.ReadInteger (address);
			address += int_size;

			if (new_num_range_entries == num_range_entries)
				return false;

			int count = new_num_range_entries - num_range_entries;
			ITargetMemoryReader range_reader = memory.ReadMemory (
				range_table + num_range_entries * range_entry_size,
				count * range_entry_size);

			ArrayList new_ranges = MethodRangeEntry.ReadRanges (
				this, range_reader, count, offset_table);

			ranges.AddRange (new_ranges);
			num_range_entries = new_num_range_entries;
			return true;
		}

		bool update_types (ref TargetAddress address)
		{
			TargetAddress type_table = memory.ReadAddress (address);
			address += address_size;
			int type_entry_size = memory.ReadInteger (address);
			address += int_size;
			int new_num_type_entries = memory.ReadInteger (address);
			address += int_size;

			if (new_num_type_entries == num_type_entries)
				return false;

			int count = new_num_type_entries - num_type_entries;
			ITargetMemoryReader type_reader = memory.ReadMemory (
				type_table + num_type_entries * type_entry_size,
				count * type_entry_size);

			TypeEntry.ReadTypes (this, type_reader, count);

			num_type_entries = new_num_type_entries;
			return true;
		}

		public bool Update ()
		{
			TargetAddress address = dynamic_address;
			if (memory.ReadInteger (address) != 0)
				return false;
			address += int_size;

			int new_generation = memory.ReadInteger (address);
			if (new_generation == generation)
				return false;
			address += int_size;

			generation = new_generation;

			bool updated = false;

			updated |= update_ranges (ref address);
			updated |= update_types (ref address);

			return true;
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

		internal ISymbolLookup GetMethod (long offset, TargetAddress dynamic_address,
						  int dynamic_size)
		{
			int index = CheckMethodOffset (offset);
			reader.Position = offset;
			MethodEntry method = new MethodEntry (reader);
			string_reader.Offset = index * int_size;
			string_reader.Offset = string_reader.ReadInteger ();

			StringBuilder sb = new StringBuilder ();
			while (true) {
				byte b = string_reader.ReadByte ();
				if (b == 0)
					break;

				sb.Append ((char) b);
			}

			ITargetMemoryReader dynamic_reader = memory.ReadMemory (
				dynamic_address, dynamic_size);

			return new MonoMethod (this, method, sb.ToString (), dynamic_reader);
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
			SourceFileFactory factory;
			System.Reflection.MethodBase rmethod;
			MonoType this_type;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			MethodAddress address;

			static MethodInfo get_method;
			static MethodInfo get_local_type_from_sig;

			static MonoMethod ()
			{
				Type type = typeof (Assembly);
				get_method = type.GetMethod ("MonoDebugger_GetMethod");
				if (get_method == null)
					throw new InternalError (
						"Can't find Assembly.MonoDebugger_GetMethod");
				get_local_type_from_sig = type.GetMethod ("MonoDebugger_GetLocalTypeFromSignature");
				if (get_local_type_from_sig == null)
					throw new InternalError (
						"Can't find Assembly.MonoDebugger_GetLocalTypeFromSignature");

			}

			public MonoMethod (MonoSymbolTableReader reader, MethodEntry method,
					   string name, ITargetMemoryReader dynamic_reader)
				: base (name, reader.ImageFile, method.Token >> 24 == 6)
			{
				this.reader = reader;
				this.method = method;
				this.factory = new SourceFileFactory ();

				address = new MethodAddress (method, dynamic_reader.BinaryReader);

				TargetAddress start = new TargetAddress (
					reader.inferior, address.StartAddress);
				TargetAddress end = new TargetAddress (
						reader.inferior, address.EndAddress);
				SetAddresses (start, end);

				object[] args = new object[] { (int) method.Token };
				rmethod = (System.Reflection.MethodBase) get_method.Invoke (
					reader.assembly, args);
			}

			void get_variables ()
			{
				if (has_variables)
					return;

				if (address.ThisTypeInfoAddress != 0)
					this_type = reader.table.GetType (
						rmethod.ReflectedType, 0, address.ThisTypeInfoAddress);

				ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					param_types [i] = reader.table.GetType (
						param_info [i].ParameterType,
						address.ParamVariableInfo [i].Size,
						address.ParamTypeInfoAddresses [i]);

				parameters = new IVariable [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					parameters [i] = new MonoVariable (
						reader.backend, param_info [i].Name, param_types [i],
						false, this, address.ParamVariableInfo [i]);

				local_types = new MonoType [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					LocalVariableEntry local = method.Locals [i];

					object[] args = new object[] { local.Signature };
					Type type = (Type) get_local_type_from_sig.Invoke (
						reader.assembly, args);

					local_types [i] = reader.table.GetType (
						type, address.LocalVariableInfo [i].Size,
						address.LocalTypeInfoAddresses [i]);
				}

				locals = new IVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					LocalVariableEntry local = method.Locals [i];

					locals [i] = new MonoVariable (
						reader.backend, local.Name, local_types [i],
						true, this, address.LocalVariableInfo [i]);
				}

				has_variables = true;
			}

			protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
								     out ArrayList addresses)
			{
				ISourceBuffer retval = CSharpMethod.GetMethodSource (
					this, method, address.LineAddresses, factory,
					out start_row, out end_row, out addresses);

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
					return rmethod;
				}
			}

			public override IVariable[] Parameters {
				get {
					get_variables ();
					return parameters;
				}
			}

			public override IVariable[] Locals {
				get {
					get_variables ();
					return locals;
				}
			}
		}

		private class MethodRangeEntry : SymbolRangeEntry
		{
			MonoSymbolTableReader reader;
			int file_offset;
			TargetAddress dynamic_address;
			int dynamic_size;

			private MethodRangeEntry (MonoSymbolTableReader reader, int file_offset,
						  TargetAddress dynamic_address, int dynamic_size,
						  TargetAddress start_address, TargetAddress end_address)
				: base (start_address, end_address)
			{
				this.reader = reader;
				this.file_offset = file_offset;
				this.dynamic_address = dynamic_address;
				this.dynamic_size = dynamic_size;
			}

			public static ArrayList ReadRanges (MonoSymbolTableReader reader,
							    ITargetMemoryReader memory, int count,
							    OffsetTable offset_table)
			{
				ArrayList list = new ArrayList ();

				for (int i = 0; i < count; i++) {
					long start = memory.ReadLongInteger ();
					long end = memory.ReadLongInteger ();
					int offset = memory.ReadInteger ();
					TargetAddress dynamic_address = memory.ReadAddress ();
					int dynamic_size = memory.ReadInteger ();

					reader.CheckMethodOffset (offset);

					TargetAddress tstart = new TargetAddress (reader.inferior, start);
					TargetAddress tend = new TargetAddress (reader.inferior, end);

					list.Add (new MethodRangeEntry (reader, offset, dynamic_address,
									dynamic_size, tstart, tend));
				}

				return list;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return reader.GetMethod (file_offset, dynamic_address, dynamic_size);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry [{0:x}:{1:x}:{2:x}:{3:x}:{4:x}]",
						      StartAddress, EndAddress, file_offset,
						      dynamic_address, dynamic_size);
			}
		}
	}

	internal class MonoCSharpLanguageBackend : SymbolTable, ILanguageBackend
	{
		IInferior inferior;
		DebuggerBackend backend;
		MonoDebuggerInfo info;
		int symtab_generation;
		ArrayList ranges;
		TargetAddress trampoline_address;
		IArchitecture arch;
		MonoSymbolFileTable table;

		public MonoCSharpLanguageBackend (DebuggerBackend backend, IInferior inferior)
		{
			this.backend = backend;
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
			long magic = header.ReadLongInteger ();
			if (magic != MonoSymbolFileTable.DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version != MonoSymbolFileTable.DynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, but expected {1}.",
					version, MonoSymbolFileTable.DynamicVersion);

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
				if ((table != null) && (generation == symtab_generation)) {
					do_update_table ();
					return;
				}
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
				ranges = null;
				table = null;
				return;
			}

			try {
				updating_symfiles = true;

				if ((inferior.State != TargetState.CORE_FILE) &&
				    ((int) inferior.CallMethod (info.update_symbol_file_table, 0) == 0)) {
					// Nothing to do.
					return;
				}

				do_update_symbol_files ();
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
				ranges = null;
				table = null;
			} finally {
				updating_symfiles = false;
				base.UpdateSymbolTable ();
			}
		}

		void do_update_symbol_files ()
		{
			Console.WriteLine ("Re-reading symbol files.");

			ranges = new ArrayList ();

			TargetAddress address = inferior.ReadAddress (info.symbol_file_table);
			if (address.Address == 0) {
				Console.WriteLine ("Ooops, no symtab loaded.");
				return;
			}
			table = new MonoSymbolFileTable (backend, inferior, inferior, address, this);

			symtab_generation = table.Generation;

			do_update_table ();

			Console.WriteLine ("Done re-reading symbol files.");
		}

		void do_update_table ()
		{
			if (table.Update ())
				ranges = table.SymbolRanges;
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
