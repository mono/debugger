using System;
using System.IO;
using System.Text;
using R = System.Reflection;
using System.Runtime.InteropServices;
using System.Collections;
using System.Threading;
using C = Mono.CSharp.Debugger;
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
		public readonly bool HasLivenessInfo;
		public readonly int BeginLiveness;
		public readonly int EndLiveness;

		internal enum AddressMode : long
		{
			Register	= 0,
			RegOffset	= 0x10000000,
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
			BeginLiveness = reader.ReadInt32 ();
			EndLiveness = reader.ReadInt32 ();

			Mode = (AddressMode) (Index & AddressModeFlags);
			Index = (int) ((long) Index & ~AddressModeFlags);

			if (Mode == AddressMode.Register)
				Index = register_map [Index];

			HasLivenessInfo = (BeginLiveness != 0) && (EndLiveness != 0);
		}

		public override string ToString ()
		{
			return String.Format ("[VariableInfo {0}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}]",
					      Mode, Index, Offset, Size, BeginLiveness, EndLiveness);
		}
	}

	internal struct JitLineNumberEntry
	{
		public readonly int Offset;
		public readonly int Address;

		public JitLineNumberEntry (TargetBinaryReader reader)
		{
			Offset = reader.ReadInt32 ();
			Address = reader.ReadInt32 ();
		}

		public override string ToString ()
		{
			return String.Format ("[JitLineNumberEntry {0}:{1:x}]", Offset, Address);
		}
	}

	internal struct JitLexicalBlockEntry
	{
		public readonly int StartAddress;
		public readonly int EndAddress;

		public JitLexicalBlockEntry (TargetBinaryReader reader)
		{
			StartAddress = reader.ReadInt32 ();
			EndAddress = reader.ReadInt32 ();
		}

		public override string ToString ()
		{
			return String.Format ("[JitLexicalBlockEntry {0:x}:{1:x}]", StartAddress, EndAddress);
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
		public readonly JitLexicalBlockEntry[] LexicalBlocks;
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

		public MethodAddress (C.MethodEntry entry, TargetBinaryReader reader, AddressDomain domain)
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

			int lexical_block_table_offset = reader.ReadInt32 ();

			Report.Debug (DebugFlags.MethodAddress,
				      "METHOD ADDRESS: {0} {1} {2} {3} {4} {5} {6} {7}",
				      StartAddress, EndAddress, MethodStartAddress, MethodEndAddress,
				      WrapperAddress, variables_offset, type_table_offset, num_line_numbers);

			if (num_line_numbers > 0) {
				reader.Position = line_number_offset;
				for (int i = 0; i < num_line_numbers; i++)
					LineNumbers [i] = new JitLineNumberEntry (reader);
				MethodStartAddress = StartAddress + LineNumbers [0].Address;
				MethodEndAddress = StartAddress + LineNumbers [num_line_numbers-1].Address;
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

			reader.Position = lexical_block_table_offset;
			LexicalBlocks = new JitLexicalBlockEntry [entry.LexicalBlocks.Length];
			for (int i = 0; i < LexicalBlocks.Length; i++)
				LexicalBlocks [i] = new JitLexicalBlockEntry (reader);
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{5:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress, WrapperAddress);
		}
	}

	internal class MonoBuiltinTypes
	{
		public readonly MonoObjectType ObjectType;
		public readonly MonoFundamentalType ByteType;
		public readonly MonoOpaqueType VoidType;
		public readonly MonoFundamentalType BooleanType;
		public readonly MonoFundamentalType SByteType;
		public readonly MonoFundamentalType Int16Type;
		public readonly MonoFundamentalType UInt16Type;
		public readonly MonoFundamentalType Int32Type;
		public readonly MonoFundamentalType UInt32Type;
		public readonly MonoFundamentalType IntType;
		public readonly MonoFundamentalType UIntType;
		public readonly MonoFundamentalType Int64Type;
		public readonly MonoFundamentalType UInt64Type;
		public readonly MonoFundamentalType SingleType;
		public readonly MonoFundamentalType DoubleType;
		public readonly MonoFundamentalType CharType;
		public readonly MonoStringType StringType;
		public readonly MonoClass EnumType;
		public readonly MonoClass ArrayType;
		public readonly MonoClass ExceptionType;

		protected readonly int TypeInfoSize;

		public MonoBuiltinTypes (MonoSymbolTable table, ITargetMemoryAccess memory, TargetAddress address,
					 MonoSymbolFile corlib)
		{
			int size = memory.ReadInteger (address);
			ITargetMemoryReader reader = memory.ReadMemory (address, size);

			reader.Offset = reader.TargetIntegerSize;
			TypeInfoSize = reader.ReadInteger ();
			ObjectType = (MonoObjectType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ByteType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			VoidType = (MonoOpaqueType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			BooleanType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			SByteType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int16Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt16Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int32Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt32Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			IntType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UIntType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int64Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt64Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			SingleType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			DoubleType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			CharType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			StringType = (MonoStringType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			EnumType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ArrayType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ExceptionType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
		}

		private struct BuiltinTypeInfo
		{
			public readonly TargetAddress Klass;
			public readonly TargetAddress ClassInfoAddress;
			public readonly int TypeInfo;
			public readonly int ClassInfo;
			public readonly TargetAddress TypeData;

			public BuiltinTypeInfo (ITargetMemoryReader reader)
			{
				Klass = reader.ReadGlobalAddress ();
				ClassInfoAddress = reader.ReadGlobalAddress ();
				TypeInfo = reader.ReadInteger ();
				ClassInfo = reader.ReadInteger ();
				TypeData = reader.ReadGlobalAddress ();
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})", GetType (), Klass,
						      ClassInfoAddress, TypeInfo, ClassInfo, TypeData);
			}

			public static MonoType GetType (MonoBuiltinTypes builtin, ITargetMemoryAccess memory,
							TargetAddress address, MonoSymbolFile corlib)
			{
				int size = builtin.TypeInfoSize;
				ITargetMemoryReader reader = memory.ReadMemory (address, size);

				BuiltinTypeInfo info = new BuiltinTypeInfo (reader);
				return corlib.GetType (memory, info.ClassInfoAddress);
			}
		}
	}

	// <summary>
	//   Holds all the symbol tables from the target's JIT.
	// </summary>
	internal class MonoSymbolTable : ILanguage, IDisposable
	{
		public const int  DynamicVersion = 38;
		public const long DynamicMagic   = 0x7aff65af4253d427;

		internal ArrayList SymbolFiles;
		public readonly MonoCSharpLanguageBackend Language;
		public readonly DebuggerBackend Backend;
		public readonly ITargetMemoryInfo TargetInfo;
		ArrayList ranges;
		Hashtable class_table;
		Hashtable types;
		Hashtable image_hash;
		Hashtable assembly_hash;
		ArrayList modules;
		protected Hashtable module_hash;

		MonoBuiltinTypes builtin;
		MonoSymbolFile corlib;
		TargetAddress StartAddress;
		int TotalSize;

		int address_size;

		int last_num_type_tables;
		int last_type_table_offset;
		ArrayList type_table;

		public MonoSymbolTable (DebuggerBackend backend, MonoCSharpLanguageBackend language,
					ITargetMemoryAccess memory, TargetAddress address)
		{
			this.Language = language;
			this.Backend = backend;
			this.TargetInfo = memory;

			address_size = memory.TargetAddressSize;

			modules = new ArrayList ();
			module_hash = new Hashtable ();
			image_hash = new Hashtable ();
			assembly_hash = new Hashtable ();
			type_table = new ArrayList ();

			ranges = new ArrayList ();
			types = new Hashtable ();
			class_table = new Hashtable ();

			SymbolFiles = new ArrayList ();

			TotalSize = language.MonoDebuggerInfo.SymbolTableSize;
			StartAddress = address;
		}

		internal void Update (ITargetMemoryAccess memory)
		{
			ITargetMemoryReader header = memory.ReadMemory (StartAddress, TotalSize);

			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"Dynamic section has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version != DynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but expected {1}.",
					version, DynamicVersion);

			int total_size = header.ReadInteger ();
			if (total_size != TotalSize)
				throw new InternalError ();

			header.ReadAddress ();
			TargetAddress corlib_address = header.ReadAddress ();
			TargetAddress builtin_types_address = header.ReadAddress ();

			int num_symbol_files = header.ReadInteger ();
			TargetAddress symbol_files = header.ReadAddress ();

			symbol_files += SymbolFiles.Count * address_size;

			for (int i = SymbolFiles.Count; i < num_symbol_files; i++) {
				TargetAddress address = memory.ReadAddress (symbol_files);
				symbol_files += address_size;

				MonoSymbolFile symfile = new MonoSymbolFile (
					this, Backend, memory, memory, address, Language);
				SymbolFiles.Add (symfile);

				if (address == corlib_address)
					corlib = symfile;

				string name = symfile.Assembly.GetName (true).Name;
				Module module = (Module) module_hash [name];
				if (module == null) {
					module = Backend.ModuleManager.CreateModule (name);
					module_hash.Add (name, module);
				}
				symfile.Module = module;
				if (!module.IsLoaded) {
					MonoModule mono_module = new MonoModule (module, name, symfile);
					module.ModuleData = mono_module;
					modules.Add (mono_module);
				}
			}

			int num_type_tables = header.ReadInteger ();
			int chunk_size = header.ReadInteger ();
			TargetAddress type_tables = header.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, "TYPE TABLES: {0} {1} {2} {3}",
				      last_num_type_tables, num_type_tables, chunk_size, type_tables);

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

			TargetAddress type_table_address = header.ReadAddress ();
			int type_table_total_size = header.ReadInteger ();
			int offset = header.ReadInteger ();
			int start = header.ReadInteger ();

			int size = offset - last_type_table_offset;
			int read_offset = last_type_table_offset - start;

			Report.Debug (DebugFlags.JitSymtab, "TYPE TABLE: {0} {1} {2} {3} - {4} {5}",
				      type_table_address, type_table_total_size, offset, start,
				      read_offset, size);

			if (size != 0) {
				byte[] data = memory.ReadBuffer (type_table_address + read_offset, size);
				type_table.Add (new TypeEntry (last_type_table_offset, size, data));
			}

			last_type_table_offset = offset;

			if (corlib == null)
				throw new InternalError ();
			if (builtin == null) {
				builtin = new MonoBuiltinTypes (this, memory, builtin_types_address, corlib);
				init_types ();
			}

			bool updated = false;

			foreach (MonoSymbolFile symfile in SymbolFiles) {
				if (!symfile.Module.LoadSymbols)
					continue;

				if (symfile.Update (memory))
					updated = true;
			}

			if (updated) {
				ranges = new ArrayList ();
				foreach (MonoSymbolFile symfile in SymbolFiles) {
					if (!symfile.Module.LoadSymbols)
						continue;

					ranges.AddRange (symfile.SymbolRanges);
				}
				ranges.Sort ();
			}
		}

		public MonoType GetType (Type type, int offset)
		{
			MonoType retval = (MonoType) types [type];
			if (retval != null)
				return retval;

			MonoSymbolFile reader = GetImage (type.Assembly);
			if (reader == null)
				throw new InternalError ();

			byte[] data = GetTypeInfo (offset);
			TargetBinaryReader info = new TargetBinaryReader (data, TargetInfo);
			retval = MonoType.GetType (type, info, reader);
			types.Add (type, retval);
			return retval;
		}

		public MonoType GetTypeFromClass (long klass_address)
		{
			ClassEntry entry = (ClassEntry) class_table [klass_address];

			if (entry == null) {
				Console.WriteLine ("Can't find class at address {0:x}", klass_address);
				throw new InternalError ();
			}

			return GetType (entry.Type, entry.TypeInfo);
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return TargetInfo.GlobalAddressDomain;
			}
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

		internal void AddType (ClassEntry type)
		{
			lock (this) {
				if (!class_table.Contains (type.KlassAddress.Address))
					class_table.Add (type.KlassAddress.Address, type);
			}
		}

		internal void AddImage (MonoSymbolFile reader)
		{
			lock (this) {
				image_hash.Add (reader.MonoImage.Address, reader);
				assembly_hash.Add (reader.Assembly, reader);
			}
		}

		public MonoSymbolFile GetImage (TargetAddress address)
		{
			lock (this) {
				return (MonoSymbolFile) image_hash [address.Address];
			}
		}

		public MonoSymbolFile GetImage (R.Assembly assembly)
		{
			lock (this) {
				return (MonoSymbolFile) assembly_hash [assembly];
			}
		}

		//
		// ILanguage
		//

		MonoFundamentalType integer_type;
		MonoFundamentalType long_type;
		MonoFundamentalType pointer_type;
		MonoObjectType object_type;
		MonoStringType string_type;

		void init_types ()
		{
			object_type = (MonoObjectType) builtin.ObjectType;
			string_type = (MonoStringType) builtin.StringType;
			integer_type = (MonoFundamentalType) builtin.Int32Type;
			long_type = (MonoFundamentalType) builtin.Int64Type;
			pointer_type = (MonoFundamentalType) builtin.IntType;
		}

		string ILanguage.Name {
			get { return "C#"; }
		}

		public MonoObjectType ObjectType {
			get { return object_type; }
		}

		public MonoStringType StringType {
			get { return string_type; }
		}

		ITargetFundamentalType ILanguage.IntegerType {
			get { return integer_type; }
		}

		ITargetFundamentalType ILanguage.LongIntegerType {
			get { return long_type; }
		}

		ITargetType ILanguage.PointerType {
			get { return pointer_type; }
		}

		private ITargetType LookupType (StackFrame frame, Type type, string name)
		{
			int offset = Language.LookupType (frame, name);
			if (offset == 0)
				return null;
			return GetType (type, offset);
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			if (name.IndexOf ('[') >= 0)
				return null;

			foreach (MonoSymbolFile symfile in SymbolFiles) {
				Type type = symfile.Assembly.GetType (name);
				if (type == null)
					continue;

				MonoType mtype = (MonoType) types [type];
				if (mtype != null)
					return mtype;

				return LookupType (frame, type, name);
			}

			return null;
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
					class_table = new Hashtable ();
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

		~MonoSymbolTable ()
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
			bool has_debugging_info;
			MonoSymbolFile reader;

			public MonoModule (Module module, string name, MonoSymbolFile reader)
				: base (module, name)
			{
				this.module = module;
				this.reader = reader;

				module.ModuleData = this;

				has_debugging_info = reader.File != null;

				module.ModuleChangedEvent += new ModuleEventHandler (module_changed);
				symbols_loaded = module.LoadSymbols;
				module_changed (module);
			}

			public override ILanguage Language {
				get { return reader.Table; }
			}

			public override object LanguageBackend {
				get { return reader.Table.Language; }
			}

			public MonoSymbolFile MonoSymbolFile {
				get { return reader; }
			}

			public R.Assembly Assembly {
				get { return reader.Assembly; }
			}

			public override bool SymbolsLoaded {
				get { return symbols_loaded; }
			}

			public override SourceFile[] Sources {
				get { return reader.GetSources (); }
			}

			public override bool HasDebuggingInfo {
				get { return has_debugging_info; }
			}

			protected override void ReadModuleData ()
			{ }

			public override ISymbolTable SymbolTable {
				get { return reader.SymbolTable; }
			}

			public override ISimpleSymbolTable SimpleSymbolTable {
				get { return reader; }
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

			public override SourceMethod FindMethod (string name)
			{
				return reader.FindMethod (name);
			}

			protected override object EnableBreakpoint (BreakpointHandle handle,
								    TargetAddress address)
			{
				return reader.Language.EnableBreakpoint (handle, address);
			}

			protected override void DisableBreakpoint (BreakpointHandle handle,
								   object data)
			{
				reader.Language.DisableBreakpoint ((int) data);
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
		public readonly TargetAddress GenericTrampolineCode;
		public readonly TargetAddress NotificationCode;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress InsertBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupType;
		public readonly TargetAddress EventData;
		public readonly TargetAddress EventArg;
		public readonly TargetAddress Heap;
		public readonly int HeapSize;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			reader.Offset = reader.TargetLongIntegerSize +
				2 * reader.TargetIntegerSize;
			GenericTrampolineCode = reader.ReadAddress ();
			NotificationCode = reader.ReadAddress ();
			SymbolTable = reader.ReadAddress ();
			SymbolTableSize = reader.ReadInteger ();
			CompileMethod = reader.ReadAddress ();
			InsertBreakpoint = reader.ReadAddress ();
			RemoveBreakpoint = reader.ReadAddress ();
			RuntimeInvoke = reader.ReadAddress ();
			CreateString = reader.ReadAddress ();
			ClassGetStaticFieldData = reader.ReadAddress ();
			LookupType = reader.ReadAddress ();
			EventData = reader.ReadAddress ();
			EventArg = reader.ReadAddress ();
			Heap = reader.ReadAddress ();
			HeapSize = reader.ReadInteger ();
			Report.Debug (DebugFlags.JitSymtab, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x}:{7:x})",
				GenericTrampolineCode, NotificationCode,
				SymbolTable, SymbolTableSize, CompileMethod,
				InsertBreakpoint, RemoveBreakpoint, RuntimeInvoke);
		}
	}

	internal class ClassEntry
	{
		public readonly TargetAddress KlassAddress;
		public readonly int Rank;
		public readonly int Token;
		public readonly int TypeInfo;
		public readonly Type Type;

		static R.MethodInfo get_type;

		static ClassEntry ()
		{
			Type type = typeof (R.Assembly);
			get_type = type.GetMethod ("MonoDebugger_GetType");
			if (get_type == null)
				throw new InternalError (
					"Can't find Assembly.MonoDebugger_GetType");
		}

		internal ClassEntry (MonoSymbolFile reader, ITargetMemoryReader memory)
		{
			KlassAddress = memory.ReadAddress ();
			Rank = memory.BinaryReader.ReadInt32 ();
			Token = memory.BinaryReader.ReadInt32 ();
			TypeInfo = memory.BinaryReader.ReadInt32 ();

			if (Token != 0) {
				object[] args = new object[] { (int) Token };
				Type = (Type) get_type.Invoke (reader.Assembly, args);
			}
		}

		public static void ReadClasses (MonoSymbolFile reader,
						ITargetMemoryReader memory, int count)
		{
			for (int i = 0; i < count; i++) {
				ClassEntry entry = new ClassEntry (reader, memory);
				reader.Table.AddType (entry);
			}
		}

		public override string ToString ()
		{
			return String.Format ("ClassEntry [{0:x}:{1}:{2:x}:{3:x}]",
					      KlassAddress, Rank, Token, TypeInfo);
		}
	}

	// <summary>
	//   A single Assembly's symbol table.
	// </summary>
	internal class MonoSymbolFile : ISimpleSymbolTable
	{
		internal readonly int Index;
		internal readonly R.Assembly Assembly;
		internal readonly MonoSymbolTable Table;
		internal readonly TargetAddress MonoImage;
		internal readonly string ImageFile;
		internal readonly C.MonoSymbolFile File;
		internal Module Module;
		internal ThreadManager ThreadManager;
		internal AddressDomain GlobalAddressDomain;
		internal ITargetInfo TargetInfo;
		internal MonoCSharpLanguageBackend Language;
		protected DebuggerBackend backend;
		protected Hashtable range_hash;
		MonoCSharpSymbolTable symtab;
		ArrayList ranges;

		TargetAddress dynamic_address;
		int class_entry_size;
		int address_size;
		int int_size;

		int generation;
		int num_range_entries;
		int num_class_entries;

		internal MonoSymbolFile (MonoSymbolTable table, DebuggerBackend backend,
						ITargetInfo target_info, ITargetMemoryAccess memory,
						TargetAddress address, MonoCSharpLanguageBackend language)
		{
			this.Table = table;
			this.TargetInfo = target_info;
			this.backend = backend;
			this.Language = language;

			ThreadManager = backend.ThreadManager;
			GlobalAddressDomain = memory.GlobalAddressDomain;

			address_size = TargetInfo.TargetAddressSize;
			int_size = TargetInfo.TargetIntegerSize;

			ranges = new ArrayList ();
			range_hash = new Hashtable ();

			Index = memory.ReadInteger (address);
			address += int_size;
			address += address_size;
			MonoImage = memory.ReadAddress (address);
			address += address_size;
			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);

			table.AddImage (this);

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			class_entry_size = memory.ReadInteger (address);
			address += int_size;
			dynamic_address = address;

			Assembly = R.Assembly.LoadFrom (ImageFile);

			File = C.MonoSymbolFile.ReadSymbolFile (Assembly);

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

			Report.Debug (DebugFlags.JitSymtab, "RANGES: {0} {1} {2}", this,
				      num_range_entries, new_num_range_entries);

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

		internal MonoType GetType (ITargetMemoryAccess memory, TargetAddress address)
		{
			ITargetMemoryReader reader = memory.ReadMemory (address, class_entry_size);
			ClassEntry entry = new ClassEntry (this, reader);

			Table.AddType (entry);
			return Table.GetType (entry.Type, entry.TypeInfo);
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

			foreach (C.SourceFileEntry source in File.Sources) {
				MonoSourceFile info = new MonoSourceFile (this, source);

				sources.Add (info);
				source_hash.Add (source, info);
			}
		}

		public SourceFile[] GetSources ()
		{
			ensure_sources ();
			SourceFile[] retval = new SourceFile [sources.Count];
			sources.CopyTo (retval, 0);
			return retval;
		}

		public SourceMethod[] MethodLookup (string name)
		{
			if (File == null)
				return null;

			int[] methods = File.MethodLookup (name);
			SourceMethod[] retval = new SourceMethod [methods.Length];

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

			C.MethodEntry entry = File.GetMethod (index);
			MonoSourceFile info = (MonoSourceFile) source_hash [entry.SourceFile];
			C.MethodSourceEntry source = File.GetMethodSource (index);

			string name = entry.FullName;
			method = new MonoSourceMethod (info, this, source, entry, name);
			method_name_hash.Add (name, method);
			method_index_hash.Add (index, method);
			return method;
		}

		public SourceMethod GetMethod (int index)
		{
			return GetMethod_internal (index);
		}

		public SourceMethod GetMethodByToken (int token)
		{
			if (File == null)
				return null;

			ensure_sources ();
			C.MethodEntry entry = File.GetMethodByToken (token);
			if (entry == null)
				return null;
			return GetMethod_internal (entry.Index);
		}

		public SourceMethod FindMethod (string name)
		{
			if (File == null)
				return null;

			ensure_sources ();

			SourceMethod method = (SourceMethod) method_name_hash [name];
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

		public string SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (MethodRangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address > range.EndAddress))
					continue;

				long offset = address - range.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				IMethod method = range.GetMethod ();
				if (offset == 0)
					return method.Name;
				else
					return String.Format ("{0}+0x{1:x}", method.Name, offset);
			}

			return null;
		}	

		private class MonoSourceFile : SourceFile
		{
			MonoSymbolFile reader;
			C.SourceFileEntry source;

			public MonoSourceFile (MonoSymbolFile reader, C.SourceFileEntry source)
				: base (reader.Module, source.FileName)
			{
				this.reader = reader;
				this.source = source;
			}

			protected override ArrayList GetMethods ()
			{
				ArrayList list = new ArrayList ();

				foreach (C.MethodSourceEntry entry in source.Methods) {
					SourceMethod method = reader.GetMethod (entry.Index);
					list.Add (method);
				}

				return list;
			}
		}

		private class MonoSourceMethod : SourceMethod
		{
			MonoSymbolFile reader;
			Hashtable load_handlers;
			int index;
			C.MethodEntry entry;
			MonoMethod method;

			public MonoSourceMethod (SourceFile info, MonoSymbolFile reader,
						 C.MethodSourceEntry source, C.MethodEntry entry, string name)
				: base (info, name, source.StartRow, source.EndRow, true)
			{
				this.reader = reader;
				this.index = source.Index;
				this.entry = entry;

				info.Module.ModuleUnLoadedEvent += new ModuleEventHandler (module_unloaded);
			}

			public C.MethodEntry Entry {
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

					R.MethodBase minfo = (R.MethodBase) method.MethodHandle;

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

			protected sealed class HandlerData : IDisposable
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
			MonoSymbolFile reader;
			SourceMethod info;
			C.MethodEntry method;
			R.MethodBase rmethod;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			bool is_loaded;
			MethodAddress address;

			public MonoMethod (MonoSymbolFile reader, SourceMethod info, C.MethodEntry method)
				: base (info.Name, reader.ImageFile, reader.Module)
			{
				this.reader = reader;
				this.info = info;
				this.method = method;
				this.rmethod = method.MethodBase;
			}

			public MonoMethod (MonoSymbolFile reader, SourceMethod info, C.MethodEntry method,
					   ITargetMemoryReader dynamic_reader)
				: this (reader, info, method)
			{
				Load (dynamic_reader.BinaryReader, reader.GlobalAddressDomain);
			}

			public MethodAddress MethodAddress {
				get {
					if (!IsLoaded)
						throw new InvalidOperationException ();

					return address;
				}
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

				IMethodSource source = new CSharpMethod (
					reader, this, info, method, address.LineNumbers);

				if (source != null)
					SetSource (source);
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				R.ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					param_types [i] = reader.Table.GetType (
						param_info [i].ParameterType, address.ParamTypeInfoOffsets [i]);

				parameters = new IVariable [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					parameters [i] = new MonoVariable (
						reader.backend, param_info [i].Name, param_types [i],
						false, this, address.ParamVariableInfo [i], 0, 0);

				local_types = new MonoType [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					Type type = method.LocalTypes [i];

					local_types [i] = reader.Table.GetType (
						type, address.LocalTypeInfoOffsets [i]);
				}

				locals = new IVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					C.LocalVariableEntry local = method.Locals [i];

					if (method.LocalNamesAmbiguous && (local.BlockIndex > 0)) {
						int index = local.BlockIndex - 1;
						JitLexicalBlockEntry block = address.LexicalBlocks [index];
						locals [i] = new MonoVariable (
							reader.backend, local.Name, local_types [i],
							true, this, address.LocalVariableInfo [i],
							block.StartAddress, block.EndAddress);
					} else {
						locals [i] = new MonoVariable (
							reader.backend, local.Name, local_types [i],
							true, this, address.LocalVariableInfo [i]);
					}
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

			public override SourceMethod GetTrampoline (TargetAddress address)
			{
				return reader.Language.GetTrampoline (address);
			}
		}

		private class MethodRangeEntry : SymbolRangeEntry
		{
			MonoSymbolFile reader;
			int index;
			byte[] contents;

			private MethodRangeEntry (MonoSymbolFile reader, int index,
						  byte[] contents, TargetAddress start_address,
						  TargetAddress end_address)
				: base (start_address, end_address)
			{
				this.reader = reader;
				this.index = index;
				this.contents = contents;
			}

			public static ArrayList ReadRanges (MonoSymbolFile reader,
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

					Report.Debug (DebugFlags.JitSymtab,
						      "RANGE ENTRY: {0} {1} {2} {3} {4} {5} {6}",
						      reader, start, end, index, dynamic_address,
						      dynamic_size, entry);

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
			MonoSymbolFile reader;

			public MonoCSharpSymbolTable (MonoSymbolFile reader)
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
		TargetAddress trampoline_address;
		TargetAddress notification_address;
		bool initialized;
		ManualResetEvent reload_event;
		Mutex mutex;
		protected MonoSymbolTable table;
		Heap heap;

		public MonoCSharpLanguageBackend (DebuggerBackend backend, Process process)
		{
			this.backend = backend;
			this.process = process;

			breakpoints = new Hashtable ();
			reload_event = new ManualResetEvent (false);

			mutex = new Mutex ();

			process.TargetExitedEvent += new TargetExitedHandler (child_exited);
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

		public Heap DataHeap {
			get { return heap; }
		}

		void child_exited ()
		{
			process = null;
			info = null;
			initialized = false;
			trampoline_address = TargetAddress.Null;

			if (table != null) {
				table.Dispose ();
				table = null;
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
			if (magic != MonoSymbolTable.DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version != MonoSymbolTable.DynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, but expected {1}.",
					version, MonoSymbolTable.DynamicVersion);

			int size = (int) header.ReadInteger ();

			ITargetMemoryReader table = memory.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			trampoline_address = memory.ReadGlobalAddress (info.GenericTrampolineCode);
			notification_address = memory.ReadGlobalAddress (info.NotificationCode);

			heap = new Heap ((ITargetInfo) memory, info.Heap, info.HeapSize);
		}

		void do_update_symbol_table (ITargetMemoryAccess memory, bool force_update)
		{
			backend.ModuleManager.Lock ();
			try {
				TargetAddress address = memory.ReadAddress (info.SymbolTable);
				if (address.IsNull) {
					Console.WriteLine ("Ooops, no symtab loaded.");
					return;
				}

				if (table == null)
					table = new MonoSymbolTable (backend, this, memory, address);

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
			long retval = process.CallMethod (info.InsertBreakpoint, 0, method_name);

			int index = (int) retval;

			if (index <= 0)
				return -1;

			breakpoints.Add (index, new MyBreakpointHandle (index, handler, user_data));
			return index;
		}

		bool check_breakpoint_hit (StackFrame frame, int index, object user_data)
		{
			BreakpointHandle handle = (BreakpointHandle) user_data;

			return handle.Breakpoint.CheckBreakpointHit (frame);
		}

		void breakpoint_hit (StackFrame frame, int index, object user_data)
		{
			BreakpointHandle handle = (BreakpointHandle) user_data;

			handle.Breakpoint.BreakpointHit (frame);
		}

		internal int EnableBreakpoint (BreakpointHandle handle, TargetAddress address)
		{
			return process.InsertBreakpoint (
				handle, address, new BreakpointCheckHandler (check_breakpoint_hit),
				new BreakpointHitHandler (breakpoint_hit),
				handle.Breakpoint.HandlerNeedsFrame, handle);
		}

		internal void DisableBreakpoint (int index)
		{
			process.RemoveBreakpoint (index);
		}

		private struct MyBreakpointHandle
		{
			public readonly int Index;
			public readonly BreakpointHandler Handler;
			public readonly object UserData;

			public MyBreakpointHandle (int index, BreakpointHandler handler, object user_data)
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

		public TargetAddress RuntimeInvokeFunc {
			get {
				return info.RuntimeInvoke;
			}
		}

		TargetAddress ILanguageBackend.GetTrampoline (Inferior inferior, TargetAddress address)
		{
			IArchitecture arch = inferior.Architecture;

			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			return arch.GetTrampoline (inferior, address, trampoline_address);
		}

		TargetAddress ILanguageBackend.CompileMethodFunc {
			get {
				return info.CompileMethod;
			}
		}

		public SourceMethod GetTrampoline (TargetAddress address)
		{
			IArchitecture arch = process.Architecture;
			ITargetAccess target = process.TargetAccess;

			if (trampoline_address.IsNull)
				return null;

			TargetAddress trampoline = arch.GetTrampoline (target, address, trampoline_address);

			if (trampoline.IsNull)
				return null;

			int token = target.ReadInteger (trampoline + 4);
			TargetAddress klass = target.ReadAddress (trampoline + 8);
			TargetAddress image = target.ReadAddress (klass);
			MonoSymbolFile reader = table.GetImage (image);

			// Console.WriteLine ("TRAMPOLINE: {0} {1:x} {2} {3} {4}", trampoline, token, klass, image, reader);

			if ((reader == null) || ((token & 0xff000000) != 0x06000000))
				throw new InternalError ();

			return reader.GetMethodByToken (token);
		}

		internal int LookupType (StackFrame frame, string name)
		{
			int offset;
			mutex.WaitOne ();
			reload_event.Reset ();
			offset = (int) frame.CallMethod (info.LookupType, name).Address;
			reload_event.WaitOne ();
			mutex.ReleaseMutex ();
			return offset;
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
				TargetAddress data = runner.Inferior.ReadAddress (info.EventData);
				int arg = runner.Inferior.ReadInteger (info.EventArg);

				do_update_symbol_table (runner.Inferior, false);

				if (arg != 0) {
					if (!breakpoints.Contains (arg))
						goto done;

					MyBreakpointHandle handle = (MyBreakpointHandle) breakpoints [arg];
					handle.Handler (data, handle.UserData);
					breakpoints.Remove (arg);
					goto done;
				}
			done:
				reload_event.Set ();
			}

			return true;
		}
	}
}
