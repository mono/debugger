using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Threading;
using Mono.CSharp.Debugger;
using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Architecture;

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

		// FIXME: Map mono/arch/x86/x86-codegen.h registers to
		//        debugger/arch/IArchitectureI386.cs registers.
		int[] register_map = { (int)I386Register.EAX, (int)I386Register.ECX,
				       (int)I386Register.EDX, (int)I386Register.EBX,
				       (int)I386Register.ESP, (int)I386Register.EBP,
				       (int)I386Register.ESI, (int)I386Register.EDI };

		public VariableInfo (TargetBinaryReader reader)
		{
			Index = reader.ReadInt32 ();
			Offset = reader.ReadInt32 ();
			Size = reader.ReadInt32 ();
			BeginScope = reader.ReadInt32 ();
			EndScope = reader.ReadInt32 ();

			Mode = (AddressMode) (Index & AddressModeFlags);
			Index = (int) ((long) Index & ~AddressModeFlags);

			if (Mode == AddressMode.Register)
				Index = register_map [Index];
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
		public readonly TargetAddress WrapperAddress;
		public readonly JitLineNumberEntry[] LineNumbers;
		public readonly VariableInfo ThisVariableInfo;
		public readonly VariableInfo[] ParamVariableInfo;
		public readonly VariableInfo[] LocalVariableInfo;
		public readonly bool HasThis;
		public readonly int ClassTypeInfoOffset;
		public readonly int[] ParamTypeInfoOffsets;
		public readonly int[] LocalTypeInfoOffsets;

		protected TargetAddress ReadAddress (TargetBinaryReader reader, AddressDomain domain)
		{
			long address = reader.ReadAddress ();
			if (address != 0)
				return new TargetAddress (domain, address);
			else
				return TargetAddress.Null;
		}				

		public MethodAddress (MethodEntry entry, TargetBinaryReader reader, AddressDomain domain)
		{
			reader.Position = 4;
			StartAddress = ReadAddress (reader, domain);
			EndAddress = ReadAddress (reader, domain);
			MethodStartAddress = ReadAddress (reader, domain);
			MethodEndAddress = ReadAddress (reader, domain);
			WrapperAddress = ReadAddress (reader, domain);

			HasThis = reader.ReadInt32 () != 0;
			int variables_offset = reader.ReadInt32 ();
			int type_table_offset = reader.ReadInt32 ();

			int num_line_numbers = reader.ReadInt32 ();
			LineNumbers = new JitLineNumberEntry [num_line_numbers];

			int line_number_offset = reader.ReadInt32 ();

			Report.Debug (DebugFlags.METHOD_ADDRESS,
				      "METHOD ADDRESS: {0} {1} {2} {3} {4} {5} {6} {7}",
				      StartAddress, EndAddress, MethodStartAddress, MethodEndAddress,
				      WrapperAddress, variables_offset, type_table_offset, num_line_numbers);

			if (num_line_numbers > 0) {
				reader.Position = line_number_offset;
				for (int i = 0; i < num_line_numbers; i++)
					LineNumbers [i] = new JitLineNumberEntry (reader);
			}

			reader.Position = variables_offset;
			if (HasThis)
				ThisVariableInfo = new VariableInfo (reader);

			ParamVariableInfo = new VariableInfo [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamVariableInfo [i] = new VariableInfo (reader);

			LocalVariableInfo = new VariableInfo [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalVariableInfo [i] = new VariableInfo (reader);

			reader.Position = type_table_offset;
			ClassTypeInfoOffset = reader.ReadInt32 ();

			ParamTypeInfoOffsets = new int [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamTypeInfoOffsets [i] = reader.ReadInt32 ();

			LocalTypeInfoOffsets = new int [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalTypeInfoOffsets [i] = reader.ReadInt32 ();
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{5:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress, WrapperAddress);
		}
	}

	// <summary>
	//   Holds all the symbol tables from the target's JIT.
	// </summary>
	internal class MonoSymbolFileTable : IDisposable
	{
		public const int  DynamicVersion = 26;
		public const long DynamicMagic   = 0x7aff65af4253d427;

		internal int TotalSize;
		internal int Generation;
		internal TargetAddress GlobalSymbolFile;
		internal MonoSymbolTableReader[] SymbolFiles;
		public readonly MonoCSharpLanguageBackend Language;
		public readonly DebuggerBackend Backend;
		public readonly ITargetInfo TargetInfo;
		ArrayList ranges;
		Hashtable types;
		Hashtable type_cache;
		ArrayList modules;
		protected Hashtable module_hash;

		int address_size;
		int long_size;
		int int_size;

		int last_num_type_tables;
		int last_type_table_offset;
		ArrayList type_table;

		public MonoSymbolFileTable (DebuggerBackend backend, MonoCSharpLanguageBackend language,
					    ITargetInfo target_info)
		{
			this.Language = language;
			this.Backend = backend;
			this.TargetInfo = target_info;

			address_size = target_info.TargetAddressSize;
			long_size = target_info.TargetLongIntegerSize;
			int_size = target_info.TargetIntegerSize;

			modules = new ArrayList ();
			module_hash = new Hashtable ();
			type_table = new ArrayList ();
		}

		// <summary>
		//   Read all symbol tables from the JIT.
		// </summary>
		internal void Reload (ITargetMemoryAccess memory, TargetAddress address)
		{
			lock (this) {
				Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE: {0}", address);

				ITargetMemoryReader header = memory.ReadMemory (address, 28);

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

				GlobalSymbolFile = header.ReadAddress ();

				Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE HEADER: {0} {1} {2}",
					      TotalSize, count, Generation);

				if ((TotalSize == 0) || (count == 0)) {
					SymbolFiles = new MonoSymbolTableReader [0];
					return;
				}

				ITargetMemoryReader reader = memory.ReadMemory (address + 28, TotalSize - 28);

				Report.Debug (DebugFlags.JIT_SYMTAB, "SYMBOL FILE TABLE READER: {0}", reader);

				SymbolFiles = new MonoSymbolTableReader [count];
				for (int i = 0; i < count; i++)
					SymbolFiles [i] = new MonoSymbolTableReader (
						this, Backend, memory, memory, reader.ReadAddress (),
						Language);

				Hashtable symfile_hash = new Hashtable ();
				Hashtable references = new Hashtable ();

				foreach (MonoSymbolTableReader symfile in SymbolFiles) {
					string name = symfile.Assembly.GetName (true).Name;
					load_symfile (symfile, name);
					symfile_hash.Add (name, symfile);

					if (references.Contains (name))
						references [name] = true;
					else
						references.Add (name, true);

					AssemblyName[] refs = symfile.Assembly.GetReferencedAssemblies ();
					foreach (AssemblyName ref_name in refs) {
						if (references.Contains (ref_name.Name))
							continue;
						references.Add (ref_name.Name, false);
					}
				}

				foreach (string name in references.Keys) {
					if ((bool) references [name])
						continue;

					MonoSymbolTableReader new_symfile =
						(MonoSymbolTableReader) symfile_hash [name];
					if (new_symfile == null)
						throw new InternalError (
							"Reference to unknown module {0}", name);
					load_symfile (new_symfile, name);
					references [name] = false;
				}
			}
		}

		void load_symfile (MonoSymbolTableReader symfile, string name)
		{
			Module module = (Module) module_hash [name];
			if (module == null) {
				module = Backend.ModuleManager.CreateModule (name);
				module_hash.Add (name, module);
				symfile.Module = module;
			}
			if (!module.IsLoaded) {
				MonoModule mono_module = new MonoModule (module, name, symfile);
				module.ModuleData = mono_module;
				modules.Add (mono_module);
			}
		}

		public MonoType GetType (Type type, int type_size, int offset)
		{
			if (type_cache.Contains (offset))
				return (MonoType) type_cache [offset];

			MonoType retval;
			if (offset != 0)
				retval = MonoType.GetType (type, offset, this);
			else
				retval = new MonoOpaqueType (type, type_size);

			type_cache.Add (offset, retval);
			return retval;
		}

		public MonoType GetTypeFromClass (long klass_address)
		{
			ClassEntry entry = (ClassEntry) types [klass_address];

			if (entry == null) {
				Console.WriteLine ("Can't find class at address {0:x}", klass_address);
				throw new InternalError ();
			}

			return MonoType.GetType (entry.Type, entry.TypeInfo, this);
		}

		public AddressDomain AddressDomain {
			get {
				return Backend.ThreadManager.AddressDomain;
			}
		}

		public ArrayList SymbolRanges {
			get {
				lock (this) {
					return ranges;
				}
			}
		}

		public ICollection Modules {
			get {
				lock (this) {
					return module_hash.Values;
				}
			}
		}

		internal void AddType (ClassEntry type)
		{
			lock (this) {
				if (!types.Contains (type.KlassAddress.Address))
					types.Add (type.KlassAddress.Address, type);
			}
		}

		public bool Update (ITargetMemoryAccess memory)
		{
			lock (this) {
				bool updated = false;

				updated |= update_types (memory);

				for (int i = 0; i < SymbolFiles.Length; i++) {
					if (!SymbolFiles [i].Module.LoadSymbols)
						continue;

					if (SymbolFiles [i].Update (memory))
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
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
			// If this is a call to Dispose, dispose all managed resources.
				if (disposing) {
					SymbolFiles = null;
					if (modules != null) {
						foreach (MonoModule module in modules)
							module.Dispose ();
					}
					modules = new ArrayList ();
					module_hash = new Hashtable ();
					ranges = new ArrayList ();
					types = new Hashtable ();
					type_cache = new Hashtable ();
				}

				// Release unmanaged resources
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
				// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~MonoSymbolFileTable ()
		{
			Dispose (false);
		}

		private struct TypeEntry {
			public readonly int Offset;
			public readonly int Size;
			public readonly byte[] Data;

			public TypeEntry (ITargetMemoryAccess memory, TargetAddress address,
					  int offset, int size)
			{
				this.Offset = offset;
				this.Size = size;
				this.Data = memory.ReadBuffer (address + offset, size);
			}

			public TypeEntry (int offset, int size, byte[] data)
			{
				this.Offset = offset;
				this.Size = size;
				this.Data = data;
			}
		}

		bool update_types (ITargetMemoryAccess memory)
		{
			TargetAddress address = GlobalSymbolFile;
			int num_type_tables = memory.ReadInteger (address);
			address += int_size;
			int chunk_size = memory.ReadInteger (address);
			address += int_size;
			TargetAddress type_tables = memory.ReadAddress (address);
			address += address_size;

			if (num_type_tables != last_num_type_tables) {
				int old_offset = 0;
				int old_count = num_type_tables;
				int old_size = old_count * chunk_size;
				byte[] old_data = new byte [old_size];

				for (int i = 0; i < num_type_tables; i++) {
					TargetAddress old_table = memory.ReadAddress (type_tables);
					type_tables += address_size;

					byte[] temp_data = memory.ReadBuffer (old_table, chunk_size);
					temp_data.CopyTo (old_data, old_offset);
					old_offset += chunk_size;
				}

				last_num_type_tables = num_type_tables;
				last_type_table_offset = old_size;

				type_table = new ArrayList ();
				type_table.Add (new TypeEntry (0, old_size, old_data));
			}

			TargetAddress type_table_address = memory.ReadAddress (address);
			address += address_size;
			int total_size = memory.ReadInteger (address);
			address += int_size;
			int offset = memory.ReadInteger (address);
			address += int_size;
			int start = memory.ReadInteger (address);
			address += int_size;

			int size = offset - last_type_table_offset;
			int read_offset = last_type_table_offset - start;

			if (size == 0)
				return false;

			byte[] data = memory.ReadBuffer (type_table_address + read_offset, size);
			type_table.Add (new TypeEntry (last_type_table_offset, size, data));

			last_type_table_offset = offset;

			return false;
		}

		void merge_type_entries ()
		{
			int count = type_table.Count;
			TypeEntry last = (TypeEntry) type_table [count - 1];

			int total_size = last.Offset + last.Size;

			byte[] data = new byte [total_size];
			int offset = 0;
			for (int i = 0; i < count; i++) {
				TypeEntry entry = (TypeEntry) type_table [i];

				entry.Data.CopyTo (data, offset);
				offset += entry.Size;
			}

			type_table = new ArrayList ();
			type_table.Add (new TypeEntry (0, total_size, data));
		}

		public byte[] GetTypeInfo (int offset)
		{
			int count = type_table.Count;
			for (int i = 0; i < count; i++) {
				TypeEntry entry = (TypeEntry) type_table [i];

				if (offset >= entry.Offset + entry.Size)
					continue;

				offset -= entry.Offset;
				int size = BitConverter.ToInt32 (entry.Data, offset);

				byte[] retval = new byte [size];
				Array.Copy (entry.Data, offset + 4, retval, 0, size);
				return retval;
			}

			throw new InternalError ("Can't find type entry at offset {0}.", offset);
		}

		private class MonoModule : NativeModule, IDisposable
		{
			Module module;
			bool symbols_loaded;
			MonoSymbolTableReader reader;

			public MonoModule (Module module, string name, MonoSymbolTableReader reader)
				: base (reader.Table.Backend, module, name)
			{
				this.module = module;
				this.reader = reader;

				module.ModuleData = this;

				module.ModuleChangedEvent += new ModuleEventHandler (module_changed);
				symbols_loaded = module.LoadSymbols;
				module_changed (module);
			}

			public override object Language {
				get { return reader.Table.Language; }
			}

			public MonoSymbolTableReader MonoSymbolTableReader {
				get { return reader; }
			}

			public Assembly Assembly {
				get { return reader.Assembly; }
			}

			public override bool SymbolsLoaded {
				get { return symbols_loaded; }
			}

			public override SourceInfo[] Sources {
				get { return reader.GetSources (); }
			}

			protected override void ReadModuleData ()
			{ }

			public override ISymbolTable SymbolTable {
				get { return reader.SymbolTable; }
			}

			public override TargetAddress SimpleLookup (string name)
			{
				return TargetAddress.Null;
			}

			void module_changed (Module module)
			{
				if (module.LoadSymbols && !symbols_loaded) {
					symbols_loaded = true;
					OnSymbolsLoadedEvent ();
				} else if (!module.LoadSymbols && symbols_loaded) {
					symbols_loaded = false;
					OnSymbolsUnLoadedEvent ();
				}
			}

			public override SourceMethodInfo FindMethod (string name)
			{
				return reader.FindMethod (name);
			}

			//
			// IDisposable
			//

			private bool disposed = false;

			private void Dispose (bool disposing)
			{
				// Check to see if Dispose has already been called.
				if (!this.disposed) {
				// If this is a call to Dispose, dispose all managed resources.
					if (disposing) {
						module.ModuleData = null;
					}
				
					// Release unmanaged resources
					this.disposed = true;
				}
			}

			public void Dispose ()
			{
				Dispose (true);
				// Take yourself off the Finalization queue
				GC.SuppressFinalize (this);
			}

			~MonoModule ()
			{
				Dispose (false);
			}
		}
	}

	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress generic_trampoline_code;
		public readonly TargetAddress breakpoint_trampoline_code;
		public readonly TargetAddress symbol_file_generation;
		public readonly TargetAddress symbol_file_modified;
		public readonly TargetAddress notification_code;
		public readonly TargetAddress symbol_file_table;
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
			symbol_file_modified = reader.ReadAddress ();
			notification_code = reader.ReadAddress ();
			symbol_file_table = reader.ReadAddress ();
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
				symbol_file_generation, symbol_file_modified, symbol_file_table,
				compile_method, insert_breakpoint, remove_breakpoint,
				runtime_invoke);
		}
	}

	internal class ClassEntry
	{
		public readonly TargetAddress KlassAddress;
		public readonly int Rank;
		public readonly int Token;
		public readonly int TypeInfo;
		public readonly Type Type;

		static MethodInfo get_type;

		static ClassEntry ()
		{
			Type type = typeof (Assembly);
			get_type = type.GetMethod ("MonoDebugger_GetType");
			if (get_type == null)
				throw new InternalError (
					"Can't find Assembly.MonoDebugger_GetType");
		}

		private ClassEntry (MonoSymbolTableReader reader, ITargetMemoryReader memory)
		{
			KlassAddress = memory.ReadAddress ();
			Rank = memory.BinaryReader.ReadInt32 ();
			Token = memory.BinaryReader.ReadInt32 ();
			TypeInfo = memory.BinaryReader.ReadInt32 ();

			object[] args = new object[] { (int) Token };
			Type = (Type) get_type.Invoke (reader.Assembly, args);
		}

		public static void ReadClasses (MonoSymbolTableReader reader,
						ITargetMemoryReader memory, int count)
		{
			for (int i = 0; i < count; i++) {
				try {
					ClassEntry entry = new ClassEntry (reader, memory);
					reader.Table.AddType (entry);
				} catch (Exception e) {
					Console.WriteLine ("Can't read type: {0}", e);
					// Do nothing.
				}
			}
		}

		public override string ToString ()
		{
			return String.Format ("ClassEntry [{0:x}:{1:x}:{2:x}]",
					      KlassAddress, Token, TypeInfo);
		}
	}

	// <summary>
	//   A single Assembly's symbol table.
	// </summary>
	internal class MonoSymbolTableReader
	{
		MethodEntry[] Methods;
		internal readonly Assembly Assembly;
		internal readonly MonoSymbolFileTable Table;
		internal readonly string ImageFile;
		internal readonly MonoSymbolFile File;
		internal Module Module;
		internal ThreadManager ThreadManager;
		internal AddressDomain GlobalAddressDomain;
		internal ITargetInfo TargetInfo;
		protected MonoCSharpLanguageBackend language;
		protected DebuggerBackend backend;
		protected Hashtable range_hash;
		MonoCSharpSymbolTable symtab;
		ArrayList ranges;

		TargetAddress dynamic_address;
		TargetAddress global_symfile;
		int address_size;
		int long_size;
		int int_size;

		int generation;
		int num_range_entries;
		int num_class_entries;

		internal MonoSymbolTableReader (MonoSymbolFileTable table, DebuggerBackend backend,
						ITargetInfo target_info, ITargetMemoryAccess memory,
						TargetAddress address, MonoCSharpLanguageBackend language)
		{
			this.Table = table;
			this.TargetInfo = target_info;
			this.backend = backend;
			this.language = language;

			ThreadManager = backend.ThreadManager;
			GlobalAddressDomain = memory.GlobalAddressDomain;

			address_size = TargetInfo.TargetAddressSize;
			long_size = TargetInfo.TargetLongIntegerSize;
			int_size = TargetInfo.TargetIntegerSize;

			ranges = new ArrayList ();
			range_hash = new Hashtable ();

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
			address += int_size;

			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);
			global_symfile = memory.ReadAddress (address);
			address += address_size;

			dynamic_address = address;

			Assembly = Assembly.LoadFrom (ImageFile);

			File = MonoSymbolFile.ReadSymbolFile (Assembly);

			symtab = new MonoCSharpSymbolTable (this);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), ImageFile);
		}

		// <remarks>
		//   Each time we reload the JIT's symbol tables, add the addresses of all
		//   methods which have been JITed since the last update.
		// </remarks>
		bool update_ranges (ITargetMemoryAccess memory, ref TargetAddress address)
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
				this, memory, range_reader, count);

			ranges.AddRange (new_ranges);
			num_range_entries = new_num_range_entries;
			return true;
		}

		// <summary>
		//   Add all classes which have been created in the meantime.
		// </summary>
		bool update_classes (ITargetMemoryAccess memory, ref TargetAddress address)
		{
			TargetAddress class_table = memory.ReadAddress (address);
			address += address_size;
			int class_entry_size = memory.ReadInteger (address);
			address += int_size;
			int new_num_class_entries = memory.ReadInteger (address);
			address += int_size;

			if (new_num_class_entries == num_class_entries)
				return false;

			int count = new_num_class_entries - num_class_entries;
			ITargetMemoryReader class_reader = memory.ReadMemory (
				class_table + num_class_entries * class_entry_size,
				count * class_entry_size);

			ClassEntry.ReadClasses (this, class_reader, count);

			num_class_entries = new_num_class_entries;
			return true;
		}

		public bool Update (ITargetMemoryAccess memory)
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

			updated |= update_ranges (memory, ref address);
			updated |= update_classes (memory, ref address);

			return true;
		}

		Hashtable method_hash = new Hashtable ();

		protected MonoMethod GetMonoMethod (int index)
		{
			MonoMethod mono_method = (MonoMethod) method_hash [index];
			if (mono_method != null)
				return mono_method;

			MonoSourceMethod method = GetMethod_internal (index);

			mono_method = new MonoMethod (this, method, method.Entry);
			method_hash.Add (index, mono_method);
			return mono_method;
		}

		protected MonoMethod GetMonoMethod (int index, byte[] contents)
		{
			MonoMethod method = GetMonoMethod (index);

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetInfo);
				method.Load (reader, GlobalAddressDomain);
			}

			return method;
		}

		ArrayList sources = null;
		Hashtable source_hash = null;
		Hashtable method_index_hash = null;
		Hashtable method_name_hash = null;
		void ensure_sources ()
		{
			if (sources != null)
				return;

			sources = new ArrayList ();
			source_hash = new Hashtable ();
			method_name_hash = new Hashtable ();
			method_index_hash = new Hashtable ();

			if (File == null)
				return;

			foreach (SourceFileEntry source in File.Sources) {
				MonoSourceInfo info = new MonoSourceInfo (this, source);

				sources.Add (info);
				source_hash.Add (source, info);
			}
		}

		public SourceInfo[] GetSources ()
		{
			ensure_sources ();
			SourceInfo[] retval = new SourceInfo [sources.Count];
			sources.CopyTo (retval, 0);
			return retval;
		}

		public SourceMethodInfo[] MethodLookup (string name)
		{
			if (File == null)
				return null;

			int[] methods = File.MethodLookup (name);
			SourceMethodInfo[] retval = new SourceMethodInfo [methods.Length];

			for (int i = 0; i < methods.Length; i++)
				retval [i] = GetMethod (methods [i]);

			return retval;
		}

		MonoSourceMethod GetMethod_internal (int index)
		{
			if (File == null)
				return null;

			ensure_sources ();
			MonoSourceMethod method = (MonoSourceMethod) method_index_hash [index];
			if (method != null)
				return method;

			MethodEntry entry = File.GetMethod (index);
			MonoSourceInfo info = (MonoSourceInfo) source_hash [entry.SourceFile];
			MethodSourceEntry source = File.GetMethodSource (index);

			string name = entry.FullName;
			method = new MonoSourceMethod (info, this, source, entry, name);
			method_name_hash.Add (name, method);
			method_index_hash.Add (index, method);
			return method;
		}

		public SourceMethodInfo GetMethod (int index)
		{
			return GetMethod_internal (index);
		}

		public SourceMethodInfo FindMethod (string name)
		{
			if (File == null)
				return null;

			ensure_sources ();
			SourceMethodInfo method = (SourceMethodInfo) method_name_hash [name];
			if (method != null)
				return method;

			int method_index = File.FindMethod (name);
			if (method_index < 0)
				return null;

			return GetMethod (method_index);
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
			SourceFileEntry source;
			ArrayList methods;

			public MonoSourceInfo (MonoSymbolTableReader reader, SourceFileEntry source)
				: base (reader.Module, source.FileName)
			{
				this.reader = reader;
				this.source = source;
			}

			protected override ArrayList GetMethods ()
			{
				ArrayList list = new ArrayList ();

				foreach (MethodSourceEntry entry in source.Methods) {
					SourceMethodInfo method = reader.GetMethod (entry.Index);
					list.Add (method);
				}

				return list;
			}
		}

		private class MonoSourceMethod : SourceMethodInfo
		{
			MonoSymbolTableReader reader;
			Hashtable load_handlers;
			int index;
			string full_name;
			MethodEntry entry;
			MonoMethod method;
			MethodSourceEntry source;

			public MonoSourceMethod (SourceInfo info, MonoSymbolTableReader reader,
						 MethodSourceEntry source, MethodEntry entry, string name)
				: base (info, name, source.StartRow, source.EndRow, true)
			{
				this.reader = reader;
				this.index = source.Index;
				this.source = source;
				this.entry = entry;

				info.Module.ModuleUnLoadedEvent += new ModuleEventHandler (module_unloaded);
			}

			public MethodEntry Entry {
				get { return entry; }
			}

			void module_unloaded (Module module)
			{
				reader = null;
				method = null;
			}

			public override bool IsLoaded {
				get {
					return (method != null) &&
						((reader != null) && reader.range_hash.Contains (index));
				}
			}

			void ensure_method ()
			{
				if ((method != null) && method.IsLoaded)
					return;

				MethodRangeEntry entry = (MethodRangeEntry) reader.range_hash [index];
				method = entry.GetMethod ();
			}

			public override IMethod Method {
				get {
					if (!IsLoaded)
						throw new InvalidOperationException ();

					ensure_method ();
					return method;
				}
			}

			public override TargetAddress Lookup (int line)
			{
				if (!IsLoaded)
					throw new InvalidOperationException ();

				ensure_method ();
				if (method.HasSource)
					return method.Source.Lookup (line);
				else
					return TargetAddress.Null;
			}

			void breakpoint_hit (TargetAddress address, object user_data)
			{
				if (load_handlers == null)
					return;

				ensure_method ();

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

					if (method == null)
						method = reader.GetMonoMethod (index);
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
			SourceMethodInfo info;
			MethodEntry method;
			System.Reflection.MethodBase rmethod;
			MonoType class_type;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			bool is_loaded;
			MethodAddress address;

			public MonoMethod (MonoSymbolTableReader reader, SourceMethodInfo info, MethodEntry method)
				: base (info.Name, reader.ImageFile, reader.Module)
			{
				this.reader = reader;
				this.info = info;
				this.method = method;
				this.rmethod = method.MethodBase;
			}

			public MonoMethod (MonoSymbolTableReader reader, SourceMethodInfo info, MethodEntry method,
					   ITargetMemoryReader dynamic_reader)
				: this (reader, info, method)
			{
				Load (dynamic_reader.BinaryReader, reader.GlobalAddressDomain);
			}

			public void Load (TargetBinaryReader dynamic_reader, AddressDomain domain)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (method, dynamic_reader, domain);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);

				if (!address.WrapperAddress.IsNull)
					SetWrapperAddress (address.WrapperAddress);

				IMethodSource source = CSharpMethod.GetMethodSource (
					reader, this, method, address.LineNumbers);

				if (source != null)
					SetSource (source);
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				class_type = reader.Table.GetType (
					rmethod.ReflectedType, 0, address.ClassTypeInfoOffset);

				ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					param_types [i] = reader.Table.GetType (
						param_info [i].ParameterType,
						address.ParamVariableInfo [i].Size,
						address.ParamTypeInfoOffsets [i]);

				parameters = new IVariable [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					parameters [i] = new MonoVariable (
						reader.backend, param_info [i].Name, param_types [i],
						false, this, address.ParamVariableInfo [i]);

				local_types = new MonoType [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					Type type = method.LocalTypes [i];

					local_types [i] = reader.Table.GetType (
						type, address.LocalVariableInfo [i].Size,
						address.LocalTypeInfoOffsets [i]);
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
			int index;
			byte[] contents;

			private MethodRangeEntry (MonoSymbolTableReader reader, int index,
						  byte[] contents, TargetAddress start_address,
						  TargetAddress end_address)
				: base (start_address, end_address)
			{
				this.reader = reader;
				this.index = index;
				this.contents = contents;
			}

			public static ArrayList ReadRanges (MonoSymbolTableReader reader,
							    ITargetMemoryAccess target,
							    ITargetMemoryReader memory, int count)
			{
				ArrayList list = new ArrayList ();

				for (int i = 0; i < count; i++) {
					TargetAddress start = memory.ReadGlobalAddress ();
					TargetAddress end = memory.ReadGlobalAddress ();
					int index = memory.ReadInteger ();
					TargetAddress dynamic_address = memory.ReadAddress ();
					int dynamic_size = memory.ReadInteger ();

					byte[] contents = target.ReadBuffer (dynamic_address, dynamic_size);

					MethodRangeEntry entry = new MethodRangeEntry (
						reader, index, contents, start, end);

					list.Add (entry);
					reader.range_hash.Add (index, entry);
				}

				return list;
			}

			internal MonoMethod GetMethod ()
			{
				return reader.GetMonoMethod (index, contents);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return reader.GetMonoMethod (index, contents);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry [{0:x}:{1:x}:{2:x}]",
						      StartAddress, EndAddress, index);
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
		Process process;
		DebuggerBackend backend;
		MonoDebuggerInfo info;
		int symtab_generation;
		TargetAddress trampoline_address;
		TargetAddress notification_address;
		bool initialized;
		ManualResetEvent reload_event;
		protected MonoSymbolFileTable table;

		public MonoCSharpLanguageBackend (DebuggerBackend backend, Process process)
		{
			this.backend = backend;
			this.process = process;

			breakpoints = new Hashtable ();
			reload_event = new ManualResetEvent (false);

			process.TargetExited += new TargetExitedHandler (child_exited);
		}

		public MonoCSharpLanguageBackend (DebuggerBackend backend, Process process, CoreFile core)
			: this (backend, process)
		{
			read_mono_debugger_info (core, core.Bfd);

			do_update_symbol_table (core, true);
		}

		public string Name {
			get {
				return "Mono";
			}
		}

		public Process Process {
			get { return process; }
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get {
				return info;
			}
		}

		void child_exited ()
		{
			process = null;
			info = null;
			initialized = false;
			symtab_generation = 0;
			trampoline_address = TargetAddress.Null;

			if (table != null) {
				table.Dispose ();
				table = null;
			}
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

		void read_mono_debugger_info (ITargetMemoryAccess memory, Bfd bfd)
		{
			TargetAddress symbol_info = bfd ["MONO_DEBUGGER__debugger_info"];
			if (symbol_info.IsNull)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			ITargetMemoryReader header = memory.ReadMemory (symbol_info, 16);
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

			ITargetMemoryReader table = memory.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			trampoline_address = memory.ReadGlobalAddress (info.generic_trampoline_code);
			notification_address = memory.ReadGlobalAddress (info.notification_code);
		}

		void do_update_symbol_table (ITargetMemoryAccess memory, bool force_update)
		{
			backend.ModuleManager.Lock ();
			try {
				if (!force_update) {
					int modified = memory.ReadInteger (info.symbol_file_modified);
					if (modified == 0)
						return;

					int generation = memory.ReadInteger (info.symbol_file_generation);
					if ((table != null) && (generation == symtab_generation)) {
						table.Update (memory);
						return;
					}
				}

				TargetAddress address = memory.ReadAddress (info.symbol_file_table);
				if (address.IsNull) {
					Console.WriteLine ("Ooops, no symtab loaded.");
					return;
				}

				if (table == null)
					table = new MonoSymbolFileTable (backend, this, memory);

				table.Reload (memory, address);

				symtab_generation = table.Generation;

				table.Update (memory);
			} catch (ThreadAbortException) {
				table = null;
				return;
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
				table = null;
				return;
			} finally {
				backend.ModuleManager.UnLock ();
			}
		}

		Hashtable breakpoints = new Hashtable ();

		internal int InsertBreakpoint (string method_name, BreakpointHandler handler,
					       object user_data)
		{
			SingleSteppingEngine sse = process.SingleSteppingEngine;
			sse.AcquireThreadLock ();
			long retval;
			try {
				retval = sse.CallMethod (info.insert_breakpoint, 0, method_name);
			} finally {
				sse.ReleaseThreadLock ();
			}

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
				return trampoline_address;
			}
		}

		public TargetAddress GetTrampoline (IInferior inferior, TargetAddress address)
		{
			IArchitecture arch = inferior.Architecture;

			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			TargetAddress trampoline = arch.GetTrampoline (address, trampoline_address);

			if (trampoline.IsNull)
				return TargetAddress.Null;

			long result;
			lock (this) {
				reload_event.Reset ();
				result = inferior.CallMethod (info.compile_method, trampoline.Address);
			}
			reload_event.WaitOne ();

			TargetAddress method;
			switch (inferior.TargetAddressSize) {
			case 4:
				method = new TargetAddress (inferior.GlobalAddressDomain, (int) result);
				break;

			case 8:
				method = new TargetAddress (inferior.GlobalAddressDomain, result);
				break;
				
			default:
				throw new TargetMemoryException (
					"Unknown target address size " + inferior.TargetAddressSize);
			}

			return method;
		}

		public bool BreakpointHit (IInferior inferior, TargetAddress address)
		{
			IArchitecture arch = inferior.Architecture;

			if (info == null)
				return true;

			try {
				TargetAddress trampoline = inferior.ReadGlobalAddress (
					info.breakpoint_trampoline_code);
				if (trampoline.IsNull || (inferior.CurrentFrame != trampoline + 6))
					return true;

				TargetAddress method, code, retaddr;
				int breakpoint_id = arch.GetBreakpointTrampolineData (
					out method, out code, out retaddr);

				if (!breakpoints.Contains (breakpoint_id))
					return false;

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

		public bool DaemonThreadHandler (DaemonThreadRunner runner, TargetAddress address, int signal)
		{
			if (!initialized) {
				read_mono_debugger_info (runner.Inferior, runner.Inferior.Bfd);
				initialized = true;
			}

			if ((signal != 0) || (address != notification_address))
				return false;

			lock (this) {
				do_update_symbol_table (runner.Inferior, false);
				reload_event.Set ();
			}

			return true;
		}
	}
}
