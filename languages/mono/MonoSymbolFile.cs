using System;
using System.Collections;
using System.Globalization;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class VariableInfo
	{
		public readonly int Index;
		public readonly int Offset;
		public readonly int Size;
		public readonly AddressMode Mode;
		public readonly bool HasLivenessInfo;
		public readonly int BeginLiveness;
		public readonly int EndLiveness;
		public readonly TargetAddress MonoType;

		internal enum AddressMode : long
		{
			Register	= 0,
			RegOffset	= 0x10000000,
			TwoRegisters	= 0x20000000
		}

		const long AddressModeFlags = 0xf0000000;

		public VariableInfo (Architecture arch, TargetBinaryReader reader)
		{
			Index = reader.ReadLeb128 ();
			Offset = reader.ReadSLeb128 ();
			Size = reader.ReadLeb128 ();
			BeginLiveness = reader.ReadLeb128 ();
			EndLiveness = reader.ReadLeb128 ();

			MonoType = new TargetAddress (arch.AddressDomain, reader.ReadAddress ());

			Mode = (AddressMode) (Index & AddressModeFlags);
			Index = (int) ((long) Index & ~AddressModeFlags);

			Report.Debug (DebugFlags.JitSymtab, "VARIABLE INFO: {0} {1} {2} {3} {4}",
				      Mode, Index, Offset, Size, arch);

			if ((Mode == AddressMode.Register) || (Mode == AddressMode.RegOffset))
				Index = arch.RegisterMap [Index];

			Report.Debug (DebugFlags.JitSymtab, "VARIABLE INFO #1: {0}", Index);

			HasLivenessInfo = (BeginLiveness != 0) && (EndLiveness != 0);
		}

		public override string ToString ()
		{
			return String.Format ("[VariableInfo {0}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6}]",
					      Mode, Index, Offset, Size, BeginLiveness, EndLiveness,
					      MonoType);
		}
	}

	// managed version of struct _MonoDebugLineNumberEntry 
	internal struct JitLineNumberEntry
	{
		public readonly int Offset;
		public readonly int Address;

		public JitLineNumberEntry (int offset, int address)
		{
			this.Offset = offset;
			this.Address = address;
		}

		public override string ToString ()
		{
			return String.Format ("[JitLineNumberEntry {0}:{1:x}]", Offset, Address);
		}
	}

	// managed version of struct _MonoDebugLexicalBlockEntry
	internal struct JitLexicalBlockEntry
	{
		public readonly int StartOffset;
		public readonly int StartAddress;
		public readonly int EndOffset;
		public readonly int EndAddress;

		public JitLexicalBlockEntry (int start_offset, int start_address,
					     int end_offset, int end_address)
		{
			StartOffset = start_offset;
			StartAddress = start_address;
			EndOffset = end_offset;
			EndAddress = end_address;
		}

		public override string ToString ()
		{
			return String.Format ("[JitLexicalBlockEntry {0:x}:{1:x}-{2:x}:{3:x}]", StartOffset, StartAddress, EndOffset, EndAddress);
		}
	}


	// managed version of struct _MonoDebugMethodAddress
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

		protected TargetAddress ReadAddress (TargetBinaryReader reader, AddressDomain domain)
		{
			long address = reader.ReadAddress ();
			if (address != 0)
				return new TargetAddress (domain, address);
			else
				return TargetAddress.Null;
		}

		public MethodAddress (TargetBinaryReader reader,
				      AddressDomain domain, Architecture arch)
		{
			// here we read the MonoDebugMethodAddress structure
			// as written out in mono_debug_add_method.
			reader.Position = 16;
			ReadAddress (reader, domain); // wrapper_data
			ReadAddress (reader, domain); // method
			ReadAddress (reader, domain); // address_list
			StartAddress = ReadAddress (reader, domain);
			WrapperAddress = ReadAddress (reader, domain);
			int code_size = reader.ReadInt32 ();

			EndAddress = StartAddress + code_size;

			MethodStartAddress = StartAddress + reader.ReadLeb128 ();
			MethodEndAddress = StartAddress + reader.ReadLeb128 ();

			int num_line_numbers = reader.ReadLeb128 ();
			LineNumbers = new JitLineNumberEntry [num_line_numbers];

			int il_offset = 0, native_offset = 0;
			for (int i = 0; i < num_line_numbers; i++) {
				il_offset += reader.ReadSLeb128 ();
				native_offset += reader.ReadSLeb128 ();

				LineNumbers [i] = new JitLineNumberEntry (il_offset, native_offset);
			}

			int num_lexical_blocks = reader.ReadLeb128 ();
			LexicalBlocks = new JitLexicalBlockEntry [num_lexical_blocks];

			il_offset = 0;
			native_offset = 0;
			for (int i = 0; i < num_lexical_blocks; i ++) {
				int start_offset, end_offset, start_address, end_address;

				il_offset += reader.ReadSLeb128 ();
				start_offset = il_offset;
				native_offset += reader.ReadSLeb128 ();
				start_address = native_offset;

				il_offset += reader.ReadSLeb128 ();
				end_offset = il_offset;
				native_offset += reader.ReadSLeb128 ();
				end_address = native_offset;

				LexicalBlocks [i] = new JitLexicalBlockEntry (start_offset, start_address,
									      end_offset, end_address);
			}

			HasThis = reader.ReadByte () != 0;
			if (HasThis)
				ThisVariableInfo = new VariableInfo (arch, reader);

			int num_params = reader.ReadLeb128 ();
			ParamVariableInfo = new VariableInfo [num_params];
			for (int i = 0; i < num_params; i++)
				ParamVariableInfo [i] = new VariableInfo (arch, reader);

			int num_locals = reader.ReadLeb128 ();
			LocalVariableInfo = new VariableInfo [num_locals];
			for (int i = 0; i < num_locals; i++)
				LocalVariableInfo [i] = new VariableInfo (arch, reader);
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{5:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress, WrapperAddress);
		}
	}

	internal class MonoSymbolFile : SymbolFile
	{
		internal readonly int Index;
		internal readonly Cecil.AssemblyDefinition Assembly;
		internal readonly Cecil.ModuleDefinition ModuleDefinition;
		internal readonly TargetAddress MonoImage;
		internal readonly MonoDataTable TypeTable;
		internal readonly string ImageFile;
		internal readonly C.MonoSymbolFile File;
		internal readonly ThreadManager ThreadManager;
		internal readonly TargetInfo TargetInfo;
		internal readonly MonoLanguageBackend MonoLanguage;
		internal readonly Architecture Architecture;
		protected readonly ProcessServant process;
		MonoSymbolTable symtab;
		Module module;
		string name;

		Hashtable range_hash;
		ArrayList ranges;
		Hashtable type_hash;
		ArrayList sources;
		Hashtable source_hash;
		Hashtable source_file_hash;
		Hashtable method_index_hash;
		Hashtable function_hash;

		internal MonoSymbolFile (MonoLanguageBackend language, ProcessServant process,
					 TargetMemoryAccess memory, TargetAddress address)
		{
			this.MonoLanguage = language;
			this.TargetInfo = memory.TargetInfo;
			this.Architecture = memory.Architecture;
			this.process = process;

			ThreadManager = process.ThreadManager;

			int address_size = TargetInfo.TargetAddressSize;
			int int_size = TargetInfo.TargetIntegerSize;

			ranges = ArrayList.Synchronized (new ArrayList ());
			range_hash = Hashtable.Synchronized (new Hashtable ());
			type_hash = Hashtable.Synchronized (new Hashtable ());
			function_hash = Hashtable.Synchronized (new Hashtable ());

			Index = memory.ReadInteger (address);
			address += int_size;
			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);
			MonoImage = memory.ReadAddress (address);
			address += address_size;
			TargetAddress type_table_ptr = memory.ReadAddress (address);
			address += address_size;

			TypeTable = MonoTypeTable.CreateTypeTable (this, memory, type_table_ptr);

			try {
				Assembly = Cecil.AssemblyFactory.GetAssembly (ImageFile);
			} catch (Exception ex) {
				throw new SymbolTableException (
					"Cannot load symbol file `{0}': {1}", ImageFile, ex.Message);
			}

			ModuleDefinition = Assembly.MainModule;

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			string mdb_file = ImageFile + ".mdb";

			try {
				File = C.MonoSymbolFile.ReadSymbolFile (mdb_file);
			} catch (Exception ex) {
				Report.Error ("Cannot load symbol file `{0}': {1}", mdb_file, ex.Message);
			}

			if (File == null)
				Report.Error ("Cannot load symbol file `{0}'", mdb_file);
			else if (ModuleDefinition.Mvid != File.Guid) {
				Report.Error ("Symbol file `{0}' does not match assembly `{1}'",
					      mdb_file, ImageFile);
				File = null;
			}

			symtab = new MonoSymbolTable (this);

			name = Assembly.Name.FullName;

			module = process.Session.GetModule (name);
			if (module == null) {
				module = process.Session.CreateModule (name, this);
			} else {
				module.LoadModule (this);
			}

			process.SymbolTableManager.AddSymbolFile (this);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})",
					      GetType (), ImageFile, Module);
		}

		protected ArrayList SymbolRanges {
			get { return ranges; }
		}

		public override Module Module {
			get { return module; }
		}

		public override ISymbolTable SymbolTable {
			get { return symtab; }
		}

		public override bool IsNative {
			get { return false; }
		}

		public string Name {
			get { return name; }
		}

		public override string FullName {
			get { return name; }
		}

		public override Language Language {
			get { return MonoLanguage; }
		}

		internal override ILanguageBackend LanguageBackend {
			get { return MonoLanguage; }
		}

		public override bool SymbolsLoaded {
			get { return true; }
		}

		public override SourceFile[] Sources {
			get { return GetSources (); }
		}

		public override bool HasDebuggingInfo {
			get { return File != null; }
		}

		internal void AddRangeEntry (TargetMemoryAccess memory, TargetReader reader,
					     byte[] contents)
		{
			RangeEntry range = RangeEntry.Create (this, memory, reader, contents);
			if (!range_hash.Contains (range.Hash)) {
				range_hash.Add (range.Hash, range);
				ranges.Add (range);
			}
		}

		internal Method ReadRangeEntry (TargetMemoryAccess memory, TargetReader reader,
						byte[] contents)
		{
			RangeEntry range = RangeEntry.Create (this, memory, reader, contents);
			if (!range_hash.Contains (range.Hash)) {
				range_hash.Add (range.Hash, range);
				ranges.Add (range);
			}
#if DEBUG_VERBOSE
			Console.WriteLine ("READ RANGE ENTRY: {0}", range);
#endif
			return range.GetMethod ();
		}

		public MonoClassType LookupMonoClass (Cecil.TypeReference typeref)
		{
			TargetType type = LookupMonoType (typeref);
			if (type == null)
				return null;

			MonoClassType class_type = type as MonoClassType;
			if (class_type != null)
				return class_type;

			MonoFundamentalType fundamental = type as MonoFundamentalType;
			if (fundamental != null)
				return fundamental.ClassType;

			throw new InternalError ("UNKNOWN TYPE: {0}", type);
		}

		public TargetType LookupMonoType (Cecil.TypeReference type)
		{
			TargetType result = (TargetType) type_hash [type];
			if (result != null)
				return result;

			if (type is Cecil.ArrayType) {
				Cecil.ArrayType atype = (Cecil.ArrayType) type;
				TargetType element_type = LookupMonoType (atype.ElementType);
				result = new MonoArrayType (element_type, atype.Rank);
			} else if (type is Cecil.TypeDefinition) {
				Cecil.TypeDefinition tdef = (Cecil.TypeDefinition) type;
				if (tdef.IsEnum)
					result = new MonoEnumType (this, tdef);
				else
					result = new MonoClassType (this, tdef);
			} else {
				Console.WriteLine ("UNKNOWN TYPE: {0} {1}", type, type.GetType ());
				return null;
			}

			if (!type_hash.Contains (type))
				type_hash.Add (type, result);
			return result;
		}

		public void AddType (Cecil.TypeDefinition typedef, TargetType type)
		{
			if (!type_hash.Contains (typedef))
				type_hash.Add (typedef, type);
		}

		void ensure_sources ()
		{
			if (sources != null)
				return;

			sources = new ArrayList ();
			source_hash = new Hashtable ();
			source_file_hash = new Hashtable ();
			method_index_hash = new Hashtable ();

			if (File == null)
				return;

			foreach (C.SourceFileEntry source in File.Sources) {
				SourceFile info = new SourceFile (Module, source.FileName);

				sources.Add (info);
				source_hash.Add (info, source);
				source_file_hash.Add (source, info);
			}
		}

		public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (RangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address > range.EndAddress))
					continue;

				long offset = address - range.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				Method method = range.GetMethod ();
				return new Symbol (
					method.Name, range.StartAddress, (int) offset);
			}

			return null;
		}

		public SourceFile[] GetSources ()
		{
			ensure_sources ();
			SourceFile[] retval = new SourceFile [sources.Count];
			sources.CopyTo (retval, 0);
			return retval;
		}

		public MonoFunctionType LookupFunction (MonoClassType klass, Cecil.MethodDefinition mdef)
		{
			ensure_sources ();
			int token = MonoDebuggerSupport.GetMethodToken (mdef);
			MonoFunctionType function = (MonoFunctionType) function_hash [token];
			if (function != null)
				return function;

			C.MethodEntry entry = File != null ? File.GetMethodByToken (token) : null;
			if (entry != null) {
				C.MethodSourceEntry source = File.GetMethodSource (entry.Index);
				SourceFile file = (SourceFile) source_file_hash [entry.SourceFile];
				function = new MonoFunctionType (
					klass, mdef, file, source.StartRow, source.EndRow);
			} else {
				function = new MonoFunctionType (klass, mdef);
			}

			function_hash.Add (token, function);
			return function;
		}

		public MonoFunctionType GetFunctionByToken (int token)
		{
			ensure_sources ();
			Cecil.MethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, token);

			MonoClassType klass = LookupMonoClass (mdef.DeclaringType);
			if (klass == null)
				throw new InternalError ();

			return LookupFunction (klass, mdef);
		}

		MonoMethodSource GetMethodSource (int index)
		{
			ensure_sources ();
			MonoMethodSource method = (MonoMethodSource) method_index_hash [index];
			if (method != null)
				return method;

			C.MethodEntry entry = File.GetMethod (index);
			SourceFile file = (SourceFile) source_file_hash [entry.SourceFile];

			return CreateMethodSource (file, index);
		}

		MonoMethodSource GetMethodSource (SourceFile file, int index)
		{
			ensure_sources ();
			MonoMethodSource method = (MonoMethodSource) method_index_hash [index];
			if (method != null)
				return method;

			return CreateMethodSource (file, index);
		}

		MonoMethodSource CreateMethodSource (SourceFile file, int index)
		{
			C.MethodEntry entry = File.GetMethod (index);
			C.MethodSourceEntry source = File.GetMethodSource (index);

			Cecil.MethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, entry.Token);

			MonoClassType klass = LookupMonoClass (mdef.DeclaringType);
			if (klass == null)
				throw new InternalError ();

			MonoFunctionType function = LookupFunction (klass, mdef);

			MonoMethodSource method = new MonoMethodSource (
				this, file, entry, source, mdef, klass, function);
			method_index_hash.Add (index, method);

			return method;
		}

		public MethodSource GetMethodByToken (int token)
		{
			if (File == null)
				return null;

			ensure_sources ();
			C.MethodEntry entry = File.GetMethodByToken (token);
			if (entry == null)
				return null;
			return GetMethodSource (entry.Index);
		}

		Hashtable method_hash = Hashtable.Synchronized (new Hashtable ());
		Hashtable wrapper_hash = Hashtable.Synchronized (new Hashtable ());

		public override MethodSource[] GetMethods (SourceFile file)
		{
			ensure_sources ();
			C.SourceFileEntry source = (C.SourceFileEntry) source_hash [file];

			C.MethodSourceEntry[] entries = source.Methods;
			MethodSource[] methods = new MethodSource [entries.Length];

			for (int i = 0; i < entries.Length; i++)
				methods [i] = GetMethodSource (file, entries [i].Index);

			return methods;
		}

		// This must match mono_type_get_desc() in mono/metadata/debug-helpers.c.
		protected static string GetTypeSignature (Cecil.TypeReference t)
		{
			Cecil.ReferenceType rtype = t as Cecil.ReferenceType;
			if (rtype != null)
				return GetTypeSignature (rtype.ElementType) + "&";

			Cecil.ArrayType atype = t as Cecil.ArrayType;
			if (atype != null) {
				string etype = GetTypeSignature (atype.ElementType);
				if (atype.Rank > 1)
					return String.Format ("{0}[{1}]", etype, atype.Rank);
				else
					return etype + "[]";
			}

			switch (t.FullName) {
			case "System.Char":	return "char";
			case "System.Boolean":	return "bool";
			case "System.Byte":	return "byte";
			case "System.SByte":	return "sbyte";
			case "System.Int16":	return "int16";
			case "System.UInt16":	return "uint16";
			case "System.Int32":	return "int";
			case "System.UInt32":	return "uint";
			case "System.Int64":	return "long";
			case "System.UInt64":	return "ulong";
			case "System.Single":	return "single";
			case "System.Double":	return "double";
			case "System.String":	return "string";
			case "System.Object":	return "object";
			default:		return t.FullName;
			}
		}

		internal static string GetMethodSignature (Cecil.MethodDefinition mdef)
		{
			StringBuilder sb = new StringBuilder ("(");
			bool first = true;
			foreach (Cecil.ParameterDefinition p in mdef.Parameters) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (GetTypeSignature (p.ParameterType).Replace ('+','/'));
			}
			sb.Append (")");
			return sb.ToString ();
		}

		internal static string GetMethodName (Cecil.MethodDefinition mdef)
		{
			return mdef.DeclaringType.FullName + '.' + mdef.Name +
				GetMethodSignature (mdef);
		}

		Cecil.MethodDefinition FindCecilMethod (string full_name)
		{
			string method_name, signature;

			int pos = full_name.IndexOf ('(');
			if (pos > 0) {
				method_name = full_name.Substring (0, pos);
				signature = full_name.Substring (pos);
			} else {
				method_name = full_name;
				signature = null;
			}

			Cecil.TypeDefinitionCollection types = Assembly.MainModule.Types;
			// FIXME: Work around an API problem in Cecil.
			foreach (Cecil.TypeDefinition type in types) {
				if (!method_name.StartsWith (type.FullName))
					continue;

				if (method_name.Length <= type.FullName.Length)
					continue;

				string mname = method_name.Substring (type.FullName.Length + 1);
				foreach (Cecil.MethodDefinition method in type.Methods) {
					if (method.Name != mname)
						continue;

					if (signature == null)
						return method;

					string sig = GetMethodSignature (method);
					if (sig != signature)
						continue;

					return method;
				}
			}

			return null;
		}

		public override MethodSource FindMethod (string name)
		{
			Cecil.MethodDefinition method = FindCecilMethod (name);
			if (method == null)
				return null;

			int token = (int) (method.MetadataToken.TokenType + method.MetadataToken.RID);
			return GetMethodByToken (token);
		}

		protected MonoMethod GetMonoMethod (MethodHashEntry hash, int index, byte[] contents)
		{
			ensure_sources ();
			MonoMethod method = (MonoMethod) method_hash [hash];
			if (method == null) {
				MonoMethodSource source = GetMethodSource (index);
				method = new MonoMethod (
					this, source, hash.Domain, source.Entry, source.Method);
				method_hash.Add (hash, method);
			}

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetInfo);
				method.Load (reader, TargetInfo.AddressDomain);
			}

#if DEBUG_VERBOSE
			Console.WriteLine ("GET MONO METHOD: {0} {1} {2}", hash, index, method);
#endif

			return method;
		}

		protected WrapperMethod GetWrapperMethod (MethodHashEntry hash, WrapperEntry wrapper,
							  byte[] contents)
		{
			WrapperMethod method = (WrapperMethod) wrapper_hash [hash];
			if (method == null) {
				method = new WrapperMethod (this, hash.Domain, wrapper);
				wrapper_hash.Add (hash, method);
			}

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetInfo);
				method.Load (reader, TargetInfo.AddressDomain);
			}

			return method;
		}

		internal MonoFunctionType GetFunctionType (string class_name, int token)
		{
			MonoClassType klass = null;

			Cecil.TypeDefinitionCollection types = Assembly.MainModule.Types;
			// FIXME: Work around an API problem in Cecil.
			foreach (Cecil.TypeDefinition type in types) {
				if (type.FullName != class_name)
					continue;

				klass = LookupMonoClass (type);
				break;
			}

			if (klass == null)
				return null;

			Cecil.MethodDefinition minfo = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, token);

			return new MonoFunctionType (klass, minfo);
		}

		internal override StackFrame UnwindStack (StackFrame last_frame,
							  TargetMemoryAccess memory)
		{
			return null;
		}

		internal override void OnModuleChanged ()
		{ }

		protected override void DoDispose ()
		{
			if (File != null)
				File.Dispose ();
			base.DoDispose ();
		}

		protected class MonoTypeTable : MonoDataTable
		{
			public readonly MonoSymbolFile SymbolFile;

			protected MonoTypeTable (MonoSymbolFile file, TargetAddress ptr,
						 TargetAddress first_chunk)
				: base (ptr, first_chunk)
			{
				this.SymbolFile = file;
			}

			public static MonoTypeTable CreateTypeTable (MonoSymbolFile file,
								     TargetMemoryAccess memory,
								     TargetAddress ptr)
			{
				TargetAddress first_chunk = memory.ReadAddress (ptr + 8);
				return new MonoTypeTable (file, ptr, first_chunk);
			}

			protected override void ReadDataItem (TargetMemoryAccess memory,
							      DataItemType type, TargetReader reader)
			{
				if (type != DataItemType.Class)
					throw new InternalError (
						"Got unknown data item: {0}", type);

				reader.BinaryReader.ReadInt32 ();

				reader.BinaryReader.ReadLeb128 ();
				reader.BinaryReader.ReadLeb128 ();
				TargetAddress klass_address = reader.ReadAddress ();

				SymbolFile.MonoLanguage.GetClassInfo (memory, klass_address);
			}

			protected override string MyToString ()
			{
				return String.Format (":{0}", SymbolFile);
			}
		}

		protected class MonoMethodSource : MethodSource
		{
			protected readonly MonoSymbolFile file;
			protected readonly SourceFile source_file;
			protected readonly C.MethodEntry method;
			protected readonly C.MethodSourceEntry source;
			protected readonly Cecil.MethodDefinition mdef;
			protected readonly MonoClassType klass;
			public readonly MonoFunctionType function;
			protected readonly string full_name;

			public MonoMethodSource (MonoSymbolFile file, SourceFile source_file,
						 C.MethodEntry method, C.MethodSourceEntry source,
						 Cecil.MethodDefinition mdef, MonoClassType klass,
						 MonoFunctionType function)
			{
				this.file = file;
				this.source_file = source_file;
				this.method = method;
				this.source = source;
				this.mdef = mdef;
				this.full_name = MonoSymbolFile.GetMethodName (mdef);
				this.function = function;
				this.klass = klass;
			}

			public override Module Module {
				get { return file.Module; }
			}

			public override string Name {
				get { return full_name; }
			}

			public override bool IsManaged {
				get { return true; }
			}

			public override bool IsDynamic {
				get { return false; }
			}

			public override TargetClassType DeclaringType {
				get { return klass; }
			}

			public override TargetFunctionType Function {
				get { return function; }
			}

			public override bool HasSourceFile {
				get { return true; }
			}

			public override SourceFile SourceFile {
				get { return source_file; }
			}

			public override bool HasSourceBuffer {
				get { return false; }
			}

			public override SourceBuffer SourceBuffer {
				get { throw new InvalidOperationException (); }
			}

			public override int StartRow {
				get { return source.StartRow; }
			}

			public override int EndRow {
				get { return source.EndRow; }
			}

			internal int Index {
				get { return source.Index; }
			}

			internal C.MethodEntry Entry {
				get { return method; }
			}

			internal Cecil.MethodDefinition Method {
				get { return mdef; }
			}

			public override Method NativeMethod {
				get { throw new InvalidOperationException (); }
			}
		}
		
		protected class MonoMethod : Method
		{
			MonoSymbolFile file;
			MethodSource source;
			C.MethodEntry method;
			Cecil.MethodDefinition mdef;
			MonoClassType decl_type;
			TargetVariable this_var;
			TargetVariable[] parameters;
			TargetVariable[] locals;
			bool has_variables;
			bool has_types;
			bool is_loaded;
			MethodAddress address;
			int domain;

			public MonoMethod (MonoSymbolFile file, MethodSource source, int domain,
					   C.MethodEntry method, Cecil.MethodDefinition mdef)
				: base (source.Name, file.ImageFile, file.Module)
			{
				this.file = file;
				this.source = source;
				this.domain = domain;
				this.method = method;
				this.mdef = mdef;
			}

			public override object MethodHandle {
				get { return mdef; }
			}

			public override int Domain {
				get { return domain; }
			}

			public override bool HasSource {
				get { return true; }
			}

			public override MethodSource MethodSource {
				get { return source; }
			}

			public void Load (TargetBinaryReader dynamic_reader, AddressDomain domain)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (
					dynamic_reader, domain, file.Architecture);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);

				SetLineNumbers (new MonoMethodLineNumberTable (
					this, source, method, address.LineNumbers));
			}

			void get_types ()
			{
				if (has_types)
					return;

				decl_type = file.LookupMonoClass (mdef.DeclaringType);
				
				has_types = true;
			}

			void read_variables (TargetMemoryAccess memory)
			{
				if (has_variables)
					return;

				get_types ();

				MonoLanguageBackend mono = file.MonoLanguage;

				Cecil.ParameterDefinitionCollection param_info = mdef.Parameters;
				parameters = new TargetVariable [param_info.Count];
				for (int i = 0; i < param_info.Count; i++) {
					VariableInfo var = address.ParamVariableInfo [i];
					TargetType type = MonoType.Read (mono, memory, var.MonoType);
					if (type == null)
						type = mono.VoidType;

					parameters [i] = new MonoVariable (
						file.process, param_info [i].Name, type,
						false, type.IsByRef, this, var, 0, 0);
				}

				locals = new TargetVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					C.LocalVariableEntry local = method.Locals [i];

					VariableInfo var = address.LocalVariableInfo [local.Index];
					TargetType type = MonoType.Read (mono, memory, var.MonoType);
					if (type == null)
						type = mono.VoidType;

					if (local.BlockIndex > 0) {
						int index = local.BlockIndex - 1;
						JitLexicalBlockEntry block = address.LexicalBlocks [index];
						locals [i] = new MonoVariable (
							file.process, local.Name, type, true,
							type.IsByRef, this, var,
							block.StartAddress, block.EndAddress);
					} else {
						locals [i] = new MonoVariable (
							file.process, local.Name, type, true,
							type.IsByRef, this, var);
					}
				}

				if (address.HasThis)
					this_var = new MonoVariable (
						file.process, "this", decl_type, true,
						true, this, address.ThisVariableInfo);

				has_variables = true;
			}

			public override TargetVariable[] GetParameters (TargetMemoryAccess memory)
			{
				if (!is_loaded)
					throw new TargetException (TargetError.MethodNotLoaded);

				read_variables (memory);
				return parameters;
			}

			public override TargetVariable[] GetLocalVariables (TargetMemoryAccess memory)
			{
				if (!is_loaded)
					throw new TargetException (TargetError.MethodNotLoaded);

				read_variables (memory);
				return locals;
			}

			public override TargetClassType DeclaringType {
				get {
					get_types ();
					return decl_type;
				}
			}

			public override bool HasThis {
				get {
					get_types ();
					return !mdef.IsStatic;
				}
			}

			public override TargetVariable GetThis (TargetMemoryAccess memory)
			{
				if (!is_loaded)
					throw new TargetException (TargetError.MethodNotLoaded);

				read_variables (memory);
				return this_var;
			}

			internal override MethodSource GetTrampoline (TargetMemoryAccess memory,
								      TargetAddress address)
			{
				return file.LanguageBackend.GetTrampoline (memory, address);
			}

			public override string[] GetNamespaces ()
			{
				int index = method.NamespaceID;

				Hashtable namespaces = new Hashtable ();

				C.SourceFileEntry source = method.SourceFile;
				foreach (C.NamespaceEntry nse in source.Namespaces)
					namespaces.Add (nse.Index, nse);

				ArrayList list = new ArrayList ();

				while ((index > 0) && namespaces.Contains (index)) {
					C.NamespaceEntry ns = (C.NamespaceEntry) namespaces [index];
					list.Add (ns.Name);
					list.AddRange (ns.UsingClauses);

					index = ns.Parent;
				}

				string[] retval = new string [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}
		}

		protected abstract class MonoLineNumberTable : LineNumberTable
		{
			Method method;

			TargetAddress start, end;
			TargetAddress method_start, method_end;

			protected MonoLineNumberTable (Method method)
			{
				this.method = method;

				this.start = method.StartAddress;
				this.end = method.EndAddress;

				if (method.HasMethodBounds) {
					method_start = method.MethodStartAddress;
					method_end = method.MethodEndAddress;
				} else {
					method_start = start;
					method_end = end;
				}
			}

			ObjectCache cache = null;
			object read_line_numbers (object user_data)
			{
				return ReadLineNumbers ();
			}

			protected LineNumberTableData Data {
				get {
					if (cache == null)
						cache = new ObjectCache
							(new ObjectCacheFunc (read_line_numbers), null, 5);

					return (LineNumberTableData) cache.Data;
				}
			}

			protected abstract LineNumberTableData ReadLineNumbers ();

			private LineEntry[] Addresses {
				get {
					return Data.Addresses;
				}
			}

			private int StartRow {
				get {
					return Data.StartRow;
				}
			}

			private int EndRow {
				get {
					return Data.EndRow;
				}
			}

			public override TargetAddress Lookup (int line)
			{
				if ((Addresses == null) || (line < StartRow) || (line > EndRow))
					return TargetAddress.Null;

				for (int i = 0; i < Addresses.Length; i++) {
					LineEntry entry = (LineEntry) Addresses [i];

					if (line <= entry.Line)
						return entry.Address;
				}

				return TargetAddress.Null;
			}

			public override SourceAddress Lookup (TargetAddress address)
			{
				if (address.IsNull || (address < start) || (address >= end))
					return null;

				SourceFile file = null;
				if (method.MethodSource.HasSourceFile)
					file = method.MethodSource.SourceFile;
				SourceBuffer buffer = null;
				if (method.MethodSource.HasSourceBuffer)
					buffer = method.MethodSource.SourceBuffer;

				if (address < method_start)
					return new SourceAddress (
						file, buffer, StartRow, (int) (address - start),
						(int) (method_start - address));

				TargetAddress next_address = end;

				for (int i = Addresses.Length-1; i >= 0; i--) {
					LineEntry entry = (LineEntry) Addresses [i];

					int range = (int) (next_address - address);
					next_address = entry.Address;

					if (next_address > address)
						continue;

					int offset = (int) (address - next_address);

					return new SourceAddress (file, buffer, entry.Line, offset, range);
				}

				if (Addresses.Length > 0)
					return new SourceAddress (
						file, buffer, Addresses [0].Line, (int) (address - start),
						(int) (end - address));

				return null;
			}

			public override void DumpLineNumbers ()
			{
				Console.WriteLine ("--------");
				Console.WriteLine ("DUMPING LINE NUMBER TABLE: {0}", method);
				Console.WriteLine ("--------");
				for (int i = 0; i < Addresses.Length; i++) {
					LineEntry entry = (LineEntry) Addresses [i];
					Console.WriteLine ("{0,4} {1,4}  {2}", i,
							   entry.Line, entry.Address);
				}
				Console.WriteLine ("--------");
			}

			protected class LineNumberTableData
			{
				public readonly int StartRow;
				public readonly int EndRow;
				public readonly LineEntry[] Addresses;

				public LineNumberTableData (int start, int end, LineEntry[] addresses)
				{
					this.StartRow = start;
					this.EndRow = end;
					this.Addresses = addresses;
				}
			}
		}

		protected class MonoMethodLineNumberTable : MonoLineNumberTable
		{
			int start_row, end_row;
			JitLineNumberEntry[] line_numbers;
			C.MethodEntry entry;
			MethodSource source;
			Method method;
			Hashtable namespaces;

			public MonoMethodLineNumberTable (Method method, MethodSource source,
							  C.MethodEntry entry, JitLineNumberEntry[] jit_lnt)
				: base (method)
			{
				this.method = method;
				this.entry = entry;
				this.line_numbers = jit_lnt;
				this.source = source;
				this.start_row = entry.StartRow;
				this.end_row = entry.EndRow;
			}

			void generate_line_number (ArrayList lines, TargetAddress address, int offset,
						   ref int last_line)
			{
				for (int i = entry.NumLineNumbers - 1; i >= 0; i--) {
					C.LineNumberEntry lne = entry.LineNumbers [i];

					if (lne.Offset > offset)
						continue;

					if (lne.Row != last_line) {
						lines.Add (new LineEntry (address, lne.Row));
						last_line = lne.Row;
					}

					break;
				}
			}

			protected override LineNumberTableData ReadLineNumbers ()
			{
				ArrayList lines = new ArrayList ();
				int last_line = -1;

				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					generate_line_number (lines, method.StartAddress + lne.Address,
							      lne.Offset, ref last_line);
				}

				lines.Sort ();

				LineEntry[] addresses = new LineEntry [lines.Count];
				lines.CopyTo (addresses, 0);

				return new LineNumberTableData (start_row, end_row, addresses);
			}

			public override void DumpLineNumbers ()
			{
				base.DumpLineNumbers ();

				Console.WriteLine ();
				Console.WriteLine ("Symfile Line Numbers:");
				Console.WriteLine ("---------------------");

				for (int i = 0; i < entry.NumLineNumbers; i++) {
					C.LineNumberEntry lne = entry.LineNumbers [i];

					Console.WriteLine ("{0,4} {1,4} {2,4:x}", i,
							   lne.Row, lne.Offset);
				}

				Console.WriteLine ("---------------------");

				Console.WriteLine ();
				Console.WriteLine ("JIT Line Numbers:");
				Console.WriteLine ("-----------------");
				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					Console.WriteLine ("{0,4} {1,4:x} {2,4:x} {3,4:x}", i, lne.Offset,
							   lne.Address, method.StartAddress + lne.Address);
				}
				Console.WriteLine ("-----------------");
			}
		}

		private struct MethodHashEntry
		{
			public readonly TargetAddress Method;
			public readonly int Domain;

			public MethodHashEntry (TargetAddress method, int domain)
			{
				this.Method = method;
				this.Domain = domain;
			}

			public override string ToString ()
			{
				return String.Format ("MethodHashEntry ({0}:{1})", Method, Domain);
			}
		}

		private class RangeEntry : SymbolRangeEntry
		{
			public readonly MonoSymbolFile File;
			public readonly MethodHashEntry Hash;
			public readonly int Index;
			public readonly WrapperEntry Wrapper;
			public readonly byte[] Contents;

			private RangeEntry (MonoSymbolFile file, int domain, int index,
					    WrapperEntry wrapper, TargetAddress method,
					    TargetAddress start_address, TargetAddress end_address,
					    byte[] contents)
				: base (start_address, end_address)
			{
				this.File = file;
				this.Index = index;
				this.Hash = new MethodHashEntry (method, domain);
				this.Wrapper = wrapper;
				this.Contents = contents;
			}

			public static RangeEntry Create (MonoSymbolFile file, TargetMemoryAccess memory,
							 TargetReader reader, byte[] contents)
			{
				int domain = reader.BinaryReader.ReadInt32 ();
				int index = reader.BinaryReader.ReadInt32 ();

				TargetAddress wrapper_data = reader.ReadAddress ();
				TargetAddress method = reader.ReadAddress ();
				reader.ReadAddress (); /* address_list */
				TargetAddress code_start = reader.ReadAddress ();
				TargetAddress wrapper_addr = reader.ReadAddress ();
				int code_size = reader.BinaryReader.ReadInt32 ();

#if DEBUG_VERBOSE
				Console.WriteLine ("CREATE RANGE: {0} {1} - {2} {3} {4} - {5} {6} {7}",
						   domain, index, wrapper_data, method, address_list,
						   code_start, wrapper_addr, code_size);
#endif

				WrapperEntry wrapper = null;

				if (!wrapper_data.IsNull) {
					int wrapper_size = 4 + 3 * memory.TargetInfo.TargetAddressSize;

					TargetReader wrapper_reader = new TargetReader (
						memory.ReadMemory (wrapper_data, wrapper_size));

					TargetAddress name_address = wrapper_reader.ReadAddress ();
					TargetAddress cil_address = wrapper_reader.ReadAddress ();
					int wrapper_type = wrapper_reader.BinaryReader.ReadInt32 ();

					string name = "<" + memory.ReadString (name_address) + ">";
					string cil_code = memory.ReadString (cil_address);

					wrapper = new WrapperEntry (
						wrapper_addr, (WrapperType) wrapper_type, name, cil_code);
				}

				return new RangeEntry (
					file, domain, index, wrapper, method,
					code_start, code_start + code_size, contents);
			}

			internal Method GetMethod ()
			{
				if (Wrapper != null)
					return File.GetWrapperMethod (Hash, Wrapper, Contents);
				else
					return File.GetMonoMethod (Hash, Index, Contents);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return GetMethod ();
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry [{0}:{1}:{2}:{3:x}:{4:x}]",
						      File.ImageFile, Hash, Index,
						      StartAddress, EndAddress);
			}
		}

		private class WrapperEntry
		{
			public readonly TargetAddress WrapperMethod;
			public readonly WrapperType WrapperType;
			public readonly string Name;
			public readonly string CILCode;
			WrapperMethod method;

			public WrapperEntry (TargetAddress wrapper_method, WrapperType wrapper_type,
					     string name, string cil_code)
			{
				this.WrapperMethod = wrapper_method;
				this.WrapperType = wrapper_type;
				this.Name = name;
				this.CILCode = cil_code;
			}
			public override string ToString ()
			{
				return String.Format ("WrapperEntry [{0:x}:{1}:{2}:{3}]",
						      WrapperMethod, WrapperType, Name, CILCode);
			}
		}

		protected class WrapperMethodSource : MethodSource
		{
			protected readonly WrapperMethod wrapper;
			protected readonly SourceBuffer buffer;

			public WrapperMethodSource (WrapperMethod wrapper)
			{
				this.wrapper = wrapper;

				string[] cil_code = wrapper.Entry.CILCode.Split ('\n');
				buffer = new SourceBuffer (wrapper.Name, cil_code);
			}

			public override Module Module {
				get { return wrapper.Module; }
			}

			public override string Name {
				get { return wrapper.Name; }
			}

			public override bool IsManaged {
				get { return false; }
			}

			public override bool IsDynamic {
				get { return true; }
			}

			public override TargetClassType DeclaringType {
				get { throw new InvalidOperationException (); }
			}

			public override TargetFunctionType Function {
				get { throw new InvalidOperationException (); }
			}

			public override bool HasSourceFile {
				get { return false; }
			}

			public override SourceFile SourceFile {
				get { throw new InvalidOperationException (); }
			}

			public override bool HasSourceBuffer {
				get { return true; }
			}

			public override SourceBuffer SourceBuffer {
				get { return buffer; }
			}

			public override int StartRow {
				get { return 1; }
			}

			public override int EndRow {
				get { return buffer.Contents.Length; }
			}

			public override Method NativeMethod {
				get { return wrapper; }
			}
		}

		protected class WrapperMethod : Method
		{
			public readonly MonoSymbolFile File;
			public readonly WrapperEntry Entry;
			bool is_loaded;
			MethodAddress address;
			WrapperMethodSource source;
			int domain;

			public WrapperMethod (MonoSymbolFile file, int domain, WrapperEntry entry)
				: base (entry.Name, file.ImageFile, file.Module)
			{
				this.File = file;
				this.Entry = entry;
				this.domain = domain;
				source = new WrapperMethodSource (this);
				SetWrapperType (entry.WrapperType);
			}

			public override object MethodHandle {
				get { return Entry.WrapperMethod; }
			}

			public override int Domain {
				get { return domain; }
			}

			public override bool HasSource {
				get { return true; }
			}

			public override MethodSource MethodSource {
				get { return source; }
			}

			public override TargetClassType DeclaringType {
				get { return null; }
			}

			public override bool HasThis {
				get { return false; }
			}

			public override TargetVariable GetThis (TargetMemoryAccess memory)
			{
				throw new InvalidOperationException ();
			}

			public override TargetVariable[] GetParameters (TargetMemoryAccess memory)
			{
				return null;
			}

			public override TargetVariable[] GetLocalVariables (TargetMemoryAccess memory)
			{
				return null;
			}

			public override string[] GetNamespaces ()
			{
				return null;
			}

			public void Load (TargetBinaryReader dynamic_reader, AddressDomain domain)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (
					dynamic_reader, domain, File.Architecture);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);
				SetLineNumbers (new WrapperLineNumberTable (this, address));
			}

			internal override MethodSource GetTrampoline (TargetMemoryAccess memory,
								      TargetAddress address)
			{
				return File.LanguageBackend.GetTrampoline (memory, address);
			}
		}

		protected class WrapperLineNumberTable : MonoLineNumberTable
		{
			WrapperMethod wrapper;
			MethodAddress address;

			public WrapperLineNumberTable (WrapperMethod wrapper, MethodAddress address)
				: base (wrapper)
			{
				this.wrapper = wrapper;
				this.address = address;
			}

			void generate_line_number (ArrayList lines, TargetAddress address, int offset,
						   int[] cil_offsets, ref int last_line)
			{
				for (int i = cil_offsets.Length - 1; i >= 0; i--) {
					int cil_offset = cil_offsets [i];

					if (cil_offset > offset)
						continue;

					if (i + 1 != last_line) {
						lines.Add (new LineEntry (address, i + 1));
						last_line = i + 1;
					}

					break;
				}
			}

			protected override LineNumberTableData ReadLineNumbers ()
			{
				ArrayList lines = new ArrayList ();
				int last_line = -1;

				JitLineNumberEntry[] line_numbers = address.LineNumbers;

				string[] cil_code = wrapper.MethodSource.SourceBuffer.Contents;

				int[] cil_offsets = new int [cil_code.Length];
				int last_cil_offset = 0;
				for (int i = 0; i < cil_code.Length; i++) {
					if (!cil_code [i].StartsWith ("IL_")) {
						cil_offsets [i] = last_cil_offset;
						continue;
					}
					string offset = cil_code [i].Substring (3, 4);
					last_cil_offset = Int32.Parse (offset, NumberStyles.HexNumber);
					cil_offsets [i] = last_cil_offset;
				}

				lines.Add (new LineEntry (wrapper.StartAddress, 1));

				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					generate_line_number (lines, wrapper.StartAddress + lne.Address,
							      lne.Offset, cil_offsets, ref last_line);
				}

				lines.Sort ();

				LineEntry[] addresses = new LineEntry [lines.Count];
				lines.CopyTo (addresses, 0);

				return new LineNumberTableData (1, cil_code.Length, addresses);
			}
		}

		protected struct TypeHashEntry
		{
			public readonly int Token;

			public TypeHashEntry (Cecil.TypeDefinition type)
			{
				Token = (int) (type.MetadataToken.TokenType + type.MetadataToken.RID);
			}

			public TypeHashEntry (int token)
			{
				Token = token;
			}

			public override bool Equals (object o)
			{
				TypeHashEntry entry = (TypeHashEntry) o;
				return (entry.Token == Token);
			}

			public override int GetHashCode ()
			{
				return Token;
			}

			public override string ToString ()
			{
				return String.Format ("TypeHashEntry ({0:x})", Token);
			}
		}

		private class MonoSymbolTable : SymbolTable
		{
			MonoSymbolFile file;

			public MonoSymbolTable (MonoSymbolFile file)
			{
				this.file = file;
			}

			public override bool HasMethods {
				get { return false; }
			}

			protected override ArrayList GetMethods ()
			{
				throw new InvalidOperationException ();
			}

			public override bool HasRanges {
				get { return true; }
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ArrayList ranges = file.SymbolRanges;

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
}
