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
	internal delegate void BreakpointHandler (TargetAddress address, object user_data);

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

	internal struct JitLineNumberEntry
	{
		public readonly int Line;
		public readonly int Offset;
		public readonly int Address;

		public JitLineNumberEntry (TargetBinaryReader reader)
		{
			Line = reader.ReadInt32 ();
			Offset = reader.ReadInt32 ();
			Address = reader.ReadInt32 ();
		}
	}

	internal class MethodAddress
	{
		public readonly TargetAddress StartAddress;
		public readonly TargetAddress EndAddress;
		public readonly TargetAddress MethodStartAddress;
		public readonly TargetAddress MethodEndAddress;
		public readonly JitLineNumberEntry[] LineNumbers;
		public readonly VariableInfo ThisVariableInfo;
		public readonly VariableInfo[] ParamVariableInfo;
		public readonly VariableInfo[] LocalVariableInfo;
		public readonly TargetAddress ThisTypeInfoAddress;
		public readonly TargetAddress[] ParamTypeInfoAddresses;
		public readonly TargetAddress[] LocalTypeInfoAddresses;

		public MethodAddress (MethodEntry entry, ITargetMemoryReader reader)
		{
			reader.Offset = 4;
			StartAddress = reader.ReadAddress ();
			EndAddress = reader.ReadAddress ();
			MethodStartAddress = reader.ReadAddress ();
			MethodEndAddress = reader.ReadAddress ();

			int variables_offset = reader.BinaryReader.ReadInt32 ();
			int type_table_offset = reader.BinaryReader.ReadInt32 ();

			int num_line_numbers = reader.BinaryReader.ReadInt32 ();
			LineNumbers = new JitLineNumberEntry [num_line_numbers];

			int line_number_size = reader.BinaryReader.ReadInt32 ();
			TargetAddress line_number_address = reader.ReadAddress ();

			Report.Debug (DebugFlags.METHOD_ADDRESS,
				      "METHOD ADDRESS: {0} {1} {2} {3} {4} {5} {6} {7}",
				      StartAddress, EndAddress, MethodStartAddress, MethodEndAddress,
				      variables_offset, type_table_offset, num_line_numbers,
				      line_number_size);

			if (num_line_numbers > 0) {
				ITargetMemoryReader line_reader = reader.TargetMemoryAccess.ReadMemory (
					line_number_address, line_number_size);
				for (int i = 0; i < num_line_numbers; i++)
					LineNumbers [i] = new JitLineNumberEntry (line_reader.BinaryReader);
			}

			reader.Offset = variables_offset;
			if (entry.ThisTypeIndex != 0)
				ThisVariableInfo = new VariableInfo (reader.BinaryReader);

			ParamVariableInfo = new VariableInfo [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamVariableInfo [i] = new VariableInfo (reader.BinaryReader);

			LocalVariableInfo = new VariableInfo [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalVariableInfo [i] = new VariableInfo (reader.BinaryReader);

			reader.Offset = type_table_offset;
			if (entry.ThisTypeIndex != 0)
				ThisTypeInfoAddress = reader.ReadAddress ();

			ParamTypeInfoAddresses = new TargetAddress [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamTypeInfoAddresses [i] = reader.ReadAddress ();

			LocalTypeInfoAddresses = new TargetAddress [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalTypeInfoAddresses [i] = reader.ReadAddress ();
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress);
		}
	}

	internal class MonoSymbolFileTable
	{
		public const int  DynamicVersion = 14;
		public const long DynamicMagic   = 0x7aff65af4253d427;

		internal int TotalSize;
		internal int Generation;
		internal MonoSymbolTableReader[] SymbolFiles;
		public readonly MonoCSharpLanguageBackend Language;
		public readonly DebuggerBackend Backend;
		IInferior inferior;
		ITargetMemoryAccess memory;
		ArrayList ranges;
		Hashtable types;
		Hashtable type_cache;
		protected Hashtable modules;

		public IInferior Inferior {
			get {
				return inferior;
			}

			set {
				inferior = value;
				if (inferior != null)
					init_inferior ();
				else
					child_exited ();
			}
		}

		void init_inferior ()
		{
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			memory = inferior;
		}

		void child_exited ()
		{
			inferior = null;
			memory = null;
			SymbolFiles = null;
			ranges = new ArrayList ();
			types = new Hashtable ();
			type_cache = new Hashtable ();

			foreach (MonoModule module in modules.Values)
				module.UnLoad ();
		}

		void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		public MonoSymbolFileTable (DebuggerBackend backend, IInferior inferior,
					    MonoCSharpLanguageBackend language)
		{
			this.memory = inferior;
			this.Language = language;
			this.Backend = backend;
			this.Inferior = inferior;

			modules = new Hashtable ();
		}

		internal void Reload (TargetAddress address)
		{
			check_inferior ();
			Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE: {0}", address);

			ITargetMemoryReader header = memory.ReadMemory (address, 24);

			Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE HEADER: {0}", header);

			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"Dynamic section has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version != DynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but expected {1}.",
					version, DynamicVersion);

			ranges = new ArrayList ();
			types = new Hashtable ();
			type_cache = new Hashtable ();

			TotalSize = header.ReadInteger ();
			int count = header.ReadInteger ();
			Generation = header.ReadInteger ();

			Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE HEADER: {0} {1} {2}",
				      TotalSize, count, Generation);

			if ((TotalSize == 0) || (count == 0)) {
				SymbolFiles = new MonoSymbolTableReader [0];
				return;
			}

			ITargetMemoryReader reader = memory.ReadMemory (address + 24, TotalSize - 24);

			Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE READER: {0}", reader);

			SymbolFiles = new MonoSymbolTableReader [count];
			for (int i = 0; i < count; i++)
				SymbolFiles [i] = new MonoSymbolTableReader (
					this, Backend, Inferior, Inferior, reader.ReadAddress (), Language);

			foreach (MonoSymbolTableReader symfile in SymbolFiles) {
				AssemblyName name = symfile.Assembly.GetName ();
				MonoModule module = (MonoModule) modules [name.FullName];
				if (module == null) {
					module = new MonoModule (this, name);
					modules.Add (name.FullName, module);
				}
				symfile.Module = module;
				module.MonoSymbolTableReader = symfile;
			}

			foreach (MonoSymbolTableReader symfile in SymbolFiles) {
				MonoModule module = (MonoModule) symfile.Module;

				module.Assembly = symfile.Assembly;
				module.MonoSymbolTableReader = symfile;
				module.Inferior = Inferior;
				module.FileName = symfile.ImageFile;
			}

			foreach (MonoSymbolTableReader symfile in SymbolFiles)
				((MonoModule) symfile.Module).ReadReferences ();

			Language.ModulesChanged ();
		}

		public MonoType GetType (Type type, int type_size, TargetAddress address)
		{
			check_inferior ();
			if (type_cache.Contains (address.Address))
				return (MonoType) type_cache [address.Address];

			MonoType retval;
			if (!address.IsNull)
				retval = MonoType.GetType (type, memory, address, this);
			else
				retval = new MonoOpaqueType (type, type_size);

			type_cache.Add (address.Address, retval);
			return retval;
		}

		public MonoType GetTypeFromClass (long klass_address)
		{
			check_inferior ();
			TypeEntry entry = (TypeEntry) types [klass_address];

			if (entry == null) {
				Console.WriteLine ("Can't find class at address {0:x}", klass_address);
				throw new InternalError ();
			}

			return MonoType.GetType (entry.Type, memory, entry.TypeInfo, this);
		}

		public ArrayList SymbolRanges {
			get {
				return ranges;
			}
		}

		public ICollection Modules {
			get {
				return modules.Values;
			}
		}

		internal void AddType (TypeEntry type)
		{
			check_inferior ();
			if (!types.Contains (type.KlassAddress.Address))
				types.Add (type.KlassAddress.Address, type);
		}

		public bool Update ()
		{
			if (inferior == null)
				return false;

			bool updated = false;
			for (int i = 0; i < SymbolFiles.Length; i++) {
				if (!SymbolFiles [i].Module.LoadSymbols)
					continue;

				if (SymbolFiles [i].Update ())
					updated = true;
			}

			if (!updated)
				return false;

			ranges = new ArrayList ();
			for (int i = 0; i < SymbolFiles.Length; i++) {
				if (!SymbolFiles [i].Module.LoadSymbols)
					continue;

				ranges.AddRange (SymbolFiles [i].SymbolRanges);
			}
			ranges.Sort ();

			return true;
		}

		private class MonoModule : NativeModule
		{
			public string FileName;

			Assembly assembly;
			MonoSymbolFileTable table;
			MonoSymbolTableReader reader;

			public MonoModule (MonoSymbolFileTable table, AssemblyName name)
				: base (name.FullName, table.Backend)
			{
				this.table = table;
			}

			public override ILanguageBackend Language {
				get {
					return table.Language;
				}
			}

			public override string FullName {
				get {
					if (FileName != null)
						return FileName;
					else
						return Name;
				}
			}

			public MonoSymbolTableReader MonoSymbolTableReader {
				get {
					return reader;
				}

				set {
					reader = value;
					if (reader != null)
						OnSymbolsLoadedEvent ();
					else
						OnSymbolsUnLoadedEvent ();
				}
			}

			public Assembly Assembly {
				get {
					return assembly;
				}

				set {
					assembly = value;
				}
			}

			public override bool SymbolsLoaded {
				get {
					return LoadSymbols && (reader != null) && (table != null);
				}
			}

			public override void UnLoad ()
			{
				reader = null;
				Assembly = null;
				base.UnLoad ();
			}

			protected override void SymbolsChanged (bool loaded)
			{
				table.Update ();
				table.Language.ModulesChanged ();

				if (loaded)
					OnSymbolsLoadedEvent ();
				else
					OnSymbolsUnLoadedEvent ();
			}

			protected override SourceInfo[] GetSources ()
			{
				if (!SymbolsLoaded)
					return new SourceInfo [0];

				return reader.GetSources ();
			}

			public void ReadReferences ()
			{
				if ((table.modules == null) || (Assembly == null))
					return;

				AssemblyName[] references = Assembly.GetReferencedAssemblies ();
				foreach (AssemblyName name in references) {
					if (table.modules.Contains (name.FullName))
						continue;

					MonoModule module = new MonoModule (table, name);
					table.modules.Add (name.FullName, module);
				}
			}

			protected override ISymbolTable GetSymbolTable ()
			{
				return reader.SymbolTable;
			}
		}
	}

	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress generic_trampoline_code;
		public readonly TargetAddress breakpoint_trampoline_code;
		public readonly TargetAddress symbol_file_generation;
		public readonly TargetAddress symbol_file_table;
		public readonly TargetAddress update_symbol_file_table;
		public readonly TargetAddress compile_method;
		public readonly TargetAddress insert_breakpoint;
		public readonly TargetAddress remove_breakpoint;
		public readonly TargetAddress runtime_invoke;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			reader.Offset = reader.TargetLongIntegerSize +
				2 * reader.TargetIntegerSize;
			generic_trampoline_code = reader.ReadAddress ();
			breakpoint_trampoline_code = reader.ReadAddress ();
			symbol_file_generation = reader.ReadAddress ();
			symbol_file_table = reader.ReadAddress ();
			update_symbol_file_table = reader.ReadAddress ();
			compile_method = reader.ReadAddress ();
			insert_breakpoint = reader.ReadAddress ();
			remove_breakpoint = reader.ReadAddress ();
			runtime_invoke = reader.ReadAddress ();
			Report.Debug (DebugFlags.JIT_SYMTAB, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x}:{7:x}:{8:x})",
				generic_trampoline_code, breakpoint_trampoline_code,
				symbol_file_generation, symbol_file_table,
				update_symbol_file_table, compile_method,
				insert_breakpoint, remove_breakpoint,
				runtime_invoke);
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
			Type = (Type) get_type.Invoke (reader.Assembly, args);

			if (Type == null)
				Type = typeof (void);
			else if (Type == typeof (object))
				MonoType.GetType (Type, memory.TargetMemoryAccess, TypeInfo, reader.Table);
		}

		public static void ReadTypes (MonoSymbolTableReader reader,
					      ITargetMemoryReader memory, int count)
		{
			for (int i = 0; i < count; i++) {
				try {
					TypeEntry entry = new TypeEntry (reader, memory);
					reader.Table.AddType (entry);
				} catch (Exception e) {
					Console.WriteLine ("Can't read type: {0}", e);
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
		internal readonly Assembly Assembly;
		internal readonly MonoSymbolFileTable Table;
		internal readonly string ImageFile;
		internal readonly string SymbolFile;
		internal Module Module;
		protected OffsetTable offset_table;
		protected MonoCSharpLanguageBackend language;
		protected DebuggerBackend backend;
		protected IInferior inferior;
		protected ITargetMemoryAccess memory;
		protected Hashtable range_hash;
		MonoCSharpSymbolTable symtab;
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
						TargetAddress address, MonoCSharpLanguageBackend language)
		{
			this.Table = table;
			this.backend = backend;
			this.inferior = inferior;
			this.memory = memory;
			this.language = language;

			start_address = address;
			address_size = memory.TargetAddressSize;
			long_size = memory.TargetLongIntegerSize;
			int_size = memory.TargetIntegerSize;

			ranges = new ArrayList ();
			range_hash = new Hashtable ();

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

			Assembly = Assembly.LoadFrom (ImageFile);

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

			symtab = new MonoCSharpSymbolTable (this);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), ImageFile, SymbolFile);
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

		Hashtable method_hash;

		protected MonoMethod GetMethod (long offset)
		{
			int index = CheckMethodOffset (offset);
			reader.Position = offset;
			MethodEntry method = new MethodEntry (reader);
			string_reader.Offset = index * int_size;
			string_reader.Offset = string_reader.ReadInteger ();

			int length = string_reader.BinaryReader.ReadInt32 ();
			byte[] buffer = string_reader.BinaryReader.ReadBuffer (length);
			string name = Encoding.UTF8.GetString (buffer);

			MonoMethod mono_method = new MonoMethod (this, method, name);
			if (method_hash == null)
				method_hash = new Hashtable ();
			method_hash.Add (offset, mono_method);
			return mono_method;
		}

		protected MonoMethod GetMethod (long offset, TargetAddress dynamic_address,
						int dynamic_size)
		{
			MonoMethod method = null;
			if (method_hash != null)
				method = (MonoMethod) method_hash [offset];

			if (method == null) {
				int index = CheckMethodOffset (offset);
				reader.Position = offset;
				MethodEntry entry = new MethodEntry (reader);
				string_reader.Offset = index * int_size;
				string_reader.Offset = string_reader.ReadInteger ();

				int length = string_reader.BinaryReader.ReadInt32 ();
				byte[] buffer = string_reader.BinaryReader.ReadBuffer (length);
				string name = Encoding.UTF8.GetString (buffer);

				method = new MonoMethod (this, entry, name);
			}

			if (!method.IsLoaded) {
				ITargetMemoryReader dynamic_reader = memory.ReadMemory (
					dynamic_address, dynamic_size);
				method.Load (dynamic_reader);
			}

			return method;
		}

		public SourceInfo[] GetSources ()
		{
			Hashtable source_hash = new Hashtable ();

			reader.Position = offset_table.method_table_offset;
			for (int i = 0; i < offset_table.method_count; i++) {
				int offset = (int) reader.Position;

				MethodEntry method = new MethodEntry (reader);

				if (method.SourceFile == null)
					continue;

				string_reader.Offset = i * int_size;
				string_reader.Offset = string_reader.ReadInteger ();

				int length = string_reader.BinaryReader.ReadInt32 ();
				byte[] buffer = string_reader.BinaryReader.ReadBuffer (length);
				string name = Encoding.UTF8.GetString (buffer);

				SourceInfo source = (SourceInfo) source_hash [method.SourceFile];
				if (source == null) {
					source = new MonoSourceInfo (this, method.SourceFile);
					source_hash.Add (method.SourceFile, source);
				}

				source.AddMethod (new MonoSourceMethod (source, this, method, name, offset));
			}

			SourceInfo[] retval = new SourceInfo [source_hash.Values.Count];
			source_hash.Values.CopyTo (retval, 0);
			return retval;

		}

		internal ArrayList SymbolRanges {
			get {
				return ranges;
			}
		}

		internal ISymbolTable SymbolTable {
			get {
				return symtab;
			}
		}

		private class MonoSourceInfo : SourceInfo
		{
			MonoSymbolTableReader reader;

			public MonoSourceInfo (MonoSymbolTableReader reader, string filename)
				: base (reader.Module, filename)
			{
				this.reader = reader;
			}

			public override ITargetLocation Lookup (int line)
			{
				return null;
			}
		}

		private class MonoSourceMethod : SourceMethodInfo
		{
			MonoSymbolTableReader reader;
			Hashtable load_handlers;
			int offset;
			string full_name;
			MonoMethod method;

			public MonoSourceMethod (SourceInfo source, MonoSymbolTableReader reader,
						 MethodEntry method, string name, int offset)
				: base (source, name, method.StartRow, method.EndRow, true)
			{
				this.reader = reader;
				this.offset = offset;

				source.Module.ModuleUnLoadedEvent += new ModuleEventHandler (module_unloaded);
			}

			void module_unloaded (Module module)
			{
				reader = null;
				method = null;
			}

			public override bool IsLoaded {
				get {
					return (method != null) ||
						((reader != null) && reader.range_hash.Contains (offset));
				}
			}

			public override IMethod Method {
				get {
					if (!IsLoaded)
						throw new InvalidOperationException ();

					if ((method != null) && method.IsLoaded)
						return method;

					MethodRangeEntry entry = (MethodRangeEntry) reader.range_hash [offset];
					method = entry.GetMethod ();

					return method;
				}
			}

			void breakpoint_hit (TargetAddress address, object user_data)
			{
				if (load_handlers == null)
					return;

				foreach (HandlerData handler in load_handlers.Keys)
					handler.Handler (handler.Method, handler.UserData);

				load_handlers = null;
			}

			public override IDisposable RegisterLoadHandler (MethodLoadedHandler handler,
									 object user_data)
			{
				HandlerData data = new HandlerData (this, handler, user_data);

				if (load_handlers == null) {
					load_handlers = new Hashtable ();

					method = reader.GetMethod (offset);
					MethodInfo minfo = (MethodInfo) method.MethodHandle;

					string full_name = String.Format (
						"{0}:{1}", minfo.ReflectedType.FullName, minfo.Name);

					reader.Table.Language.InsertBreakpoint (
						full_name, new BreakpointHandler (breakpoint_hit), null);
				}

				load_handlers.Add (data, true);
				return data;
			}

			protected void UnRegisterLoadHandler (HandlerData data)
			{
				if (load_handlers == null)
					return;

				load_handlers.Remove (data);
				if (load_handlers.Count == 0)
					load_handlers = null;
			}

			private sealed class HandlerData : IDisposable
			{
				public readonly MonoSourceMethod Method;
				public readonly MethodLoadedHandler Handler;
				public readonly object UserData;

				public HandlerData (MonoSourceMethod method, MethodLoadedHandler handler,
						    object user_data)
				{
					this.Method = method;
					this.Handler = handler;
					this.UserData = user_data;
				}

				private bool disposed = false;

				private void Dispose (bool disposing)
				{
					if (!this.disposed) {
						if (disposing) {
							Method.UnRegisterLoadHandler (this);
						}
					}
						
					this.disposed = true;
				}

				public void Dispose ()
				{
					Dispose (true);
					// Take yourself off the Finalization queue
					GC.SuppressFinalize (this);
				}

				~HandlerData ()
				{
					Dispose (false);
				}
			}
		}

		protected class MonoMethod : MethodBase
		{
			MonoSymbolTableReader reader;
			MethodEntry method;
			System.Reflection.MethodBase rmethod;
			MonoType this_type;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			bool is_loaded;
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

			public MonoMethod (MonoSymbolTableReader reader, MethodEntry method, string name)
				: base (name, reader.ImageFile, reader.Module)
			{
				this.reader = reader;
				this.method = method;

				object[] args = new object[] { (int) method.Token };
				rmethod = (System.Reflection.MethodBase) get_method.Invoke (
					reader.Assembly, args);
			}

			public MonoMethod (MonoSymbolTableReader reader, MethodEntry method,
					   string name, ITargetMemoryReader dynamic_reader)
				: this (reader, method, name)
			{
				Load (dynamic_reader);
			}

			public void Load (ITargetMemoryReader dynamic_reader)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (method, dynamic_reader);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);

				IMethodSource source = CSharpMethod.GetMethodSource (
					this, method, address.LineNumbers);

				if (source != null)
					SetSource (source);
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				if (!address.ThisTypeInfoAddress.IsNull)
					this_type = reader.Table.GetType (
						rmethod.ReflectedType, 0, address.ThisTypeInfoAddress);

				ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					param_types [i] = reader.Table.GetType (
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
						reader.Assembly, args);

					local_types [i] = reader.Table.GetType (
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

			public override object MethodHandle {
				get {
					return rmethod;
				}
			}

			public override IVariable[] Parameters {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return parameters;
				}
			}

			public override IVariable[] Locals {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

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
					TargetAddress start = memory.ReadAddress ();
					TargetAddress end = memory.ReadAddress ();
					int offset = memory.ReadInteger ();
					TargetAddress dynamic_address = memory.ReadAddress ();
					int dynamic_size = memory.ReadInteger ();

					reader.CheckMethodOffset (offset);

					MethodRangeEntry entry = new MethodRangeEntry (
						reader, offset, dynamic_address, dynamic_size, start, end);

					list.Add (entry);
					reader.range_hash.Add (offset, entry);
				}

				return list;
			}

			internal MonoMethod GetMethod ()
			{
				return reader.GetMethod (file_offset, dynamic_address, dynamic_size);
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

		private class MonoCSharpSymbolTable : SymbolTable
		{
			MonoSymbolTableReader reader;

			public MonoCSharpSymbolTable (MonoSymbolTableReader reader)
			{
				this.reader = reader;
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

			public override bool HasRanges {
				get {
					return true;
				}
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ArrayList ranges = reader.SymbolRanges;
					ISymbolRange[] retval = new ISymbolRange [ranges.Count];
					ranges.CopyTo (retval, 0);
					return retval;
				}
			}

			public override void UpdateSymbolTable ()
			{
				base.UpdateSymbolTable ();
			}
		}
	}

	internal class MonoCSharpLanguageBackend : ILanguageBackend
	{
		IInferior inferior;
		DebuggerBackend backend;
		MonoDebuggerInfo info;
		int symtab_generation;
		TargetAddress trampoline_address;
		IArchitecture arch;
		protected MonoSymbolFileTable table;

		public MonoCSharpLanguageBackend (DebuggerBackend backend)
		{
			this.backend = backend;
		}

		public string Name {
			get {
				return "Mono";
			}
		}

		public IInferior Inferior {
			get {
				return inferior;
			}

			set {
				inferior = value;
				if (inferior != null)
					init_inferior ();
				else
					child_exited ();
			}
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get {
				return info;
			}
		}

		void init_inferior ()
		{
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			if (table != null)
				table.Inferior = inferior;
		}

		void child_exited ()
		{
			inferior = null;
			info = null;
			symtab_generation = 0;
			arch = null;
			trampoline_address = TargetAddress.Null;
		}

		void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		public Module[] Modules {
			get {
				if (table == null)
					return new Module [0];

				ICollection modules = table.Modules;
				if (modules == null)
					return new Module [0];

				Module[] retval = new Module [modules.Count];
				modules.CopyTo (retval, 0);
				return retval;
			}
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

			trampoline_address = inferior.ReadAddress (info.generic_trampoline_code);
			arch = inferior.Architecture;
		}

		bool updating_symfiles;
		public void UpdateSymbolTable ()
		{
			if (updating_symfiles || (inferior == null))
				return;

			read_mono_debugger_info ();

			try {
				int generation = inferior.ReadInteger (info.symbol_file_generation);
				if ((table != null) && (generation == symtab_generation)) {
					do_update_table (false);
					return;
				}
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
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
				table = null;
			} finally {
				updating_symfiles = false;
			}
		}

		void do_update_symbol_files ()
		{
			Console.WriteLine ("Re-reading symbol files.");

			TargetAddress address = inferior.ReadAddress (info.symbol_file_table);
			if (address.IsNull) {
				Console.WriteLine ("Ooops, no symtab loaded.");
				return;
			}

			bool must_update = false;
			if (table == null) {
				table = new MonoSymbolFileTable (backend, inferior, this);
				must_update = true;
			}
			table.Reload (address);

			symtab_generation = table.Generation;

			do_update_table (must_update);

			Console.WriteLine ("Done re-reading symbol files.");
		}

		void do_update_table (bool must_update)
		{
			if (table.Update ())
				must_update = true;
		}

		Hashtable breakpoints = new Hashtable ();

		internal int InsertBreakpoint (string method_name, BreakpointHandler handler,
					       object user_data)
		{
			check_inferior ();
			long retval = inferior.CallStringMethod (info.insert_breakpoint, 0, method_name);
			int index = (int) retval;

			if (index <= 0)
				return -1;

			breakpoints.Add (index, new BreakpointHandle (index, handler, user_data));
			return index;
		}

		private struct BreakpointHandle
		{
			public readonly int Index;
			public readonly BreakpointHandler Handler;
			public readonly object UserData;

			public BreakpointHandle (int index, BreakpointHandler handler, object user_data)
			{
				this.Index = index;
				this.Handler = handler;
				this.UserData = user_data;
			}
		}

		public TargetAddress GenericTrampolineCode {
			get {
				check_inferior ();
				return trampoline_address;
			}
		}

		public TargetAddress GetTrampoline (TargetAddress address)
		{
			check_inferior ();
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

		public bool BreakpointHit (TargetAddress address)
		{
			check_inferior ();

			if (info == null)
				return true;

			try {
				TargetAddress trampoline = inferior.ReadAddress (
					info.breakpoint_trampoline_code);
				if (trampoline.IsNull || (inferior.CurrentFrame != trampoline + 1))
					return true;

				TargetAddress method, code, retaddr;
				int breakpoint_id = arch.GetBreakpointTrampolineData (
					out method, out code, out retaddr);

				Console.WriteLine ("TRAMPOLINE BREAKPOINT: {0} {1} {2} {3} {4}",
						   code, method, breakpoint_id, retaddr,
						   breakpoints.Contains (breakpoint_id));

				if (!breakpoints.Contains (breakpoint_id))
					return false;

				UpdateSymbolTable ();

				BreakpointHandle handle = (BreakpointHandle) breakpoints [breakpoint_id];
				handle.Handler (code, handle.UserData);
				breakpoints.Remove (breakpoint_id);

				return false;
			} catch (Exception e) {
				Console.WriteLine ("BREAKPOINT EXCEPTION: {0}", e);
				// Do nothing.
			}
			return true;
		}

		public void Test (IMethod method)
		{
			MethodInfo minfo = method.MethodHandle as MethodInfo;
			if (minfo == null)
				return;

			Console.WriteLine ("TEST: {0}", minfo);
		}

		public void ModulesChanged ()
		{
			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		public event ModulesChangedHandler ModulesChangedEvent;
	}
}
