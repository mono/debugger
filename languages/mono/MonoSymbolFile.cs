using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Mono.Debugger;
using Mono.Debugger.Backend;

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
			TwoRegisters	= 0x20000000,
			Dead		= 0x30000000
		}

		const long AddressModeFlags = 0xf0000000;

		public VariableInfo (Architecture arch, TargetBinaryReader reader)
		{
			Index = reader.ReadLeb128 ();
			Offset = reader.ReadSLeb128 ();
			Size = reader.ReadLeb128 ();
			BeginLiveness = reader.ReadLeb128 ();
			EndLiveness = reader.ReadLeb128 ();

			MonoType = new TargetAddress (
				reader.TargetMemoryInfo.AddressDomain, reader.ReadAddress ());

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

	// managed version of struct _MonoDebugMethodAddress
	internal class MethodAddress
	{
		public readonly TargetAddress MonoMethod;
		public readonly TargetAddress StartAddress;
		public readonly TargetAddress EndAddress;
		public readonly TargetAddress MethodStartAddress;
		public readonly TargetAddress MethodEndAddress;
		public readonly TargetAddress WrapperAddress;
		public readonly List<JitLineNumberEntry> LineNumbers;
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
			MonoMethod = ReadAddress (reader, domain);
			ReadAddress (reader, domain); // address_list
			StartAddress = ReadAddress (reader, domain);
			WrapperAddress = ReadAddress (reader, domain);
			int code_size = reader.ReadInt32 ();

			EndAddress = StartAddress + code_size;

			int prologue_end = reader.ReadLeb128 ();
			int epilogue_begin = reader.ReadLeb128 ();

			MethodStartAddress = prologue_end > 0 ?
				StartAddress + prologue_end : StartAddress;
			MethodEndAddress = epilogue_begin > 0 ?
				StartAddress + epilogue_begin : EndAddress;

			int num_line_numbers = reader.ReadLeb128 ();
			LineNumbers = new List<JitLineNumberEntry> ();

			for (int i = 0; i < num_line_numbers; i++) {
				int il_offset = reader.ReadSLeb128 ();
				int native_offset = reader.ReadSLeb128 ();

				if (il_offset < 0)
					continue;

				LineNumbers.Add (new JitLineNumberEntry (il_offset, native_offset));
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
					      StartAddress, EndAddress, LineNumbers.Count,
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
		internal readonly TargetMemoryInfo TargetMemoryInfo;
		internal readonly MonoLanguageBackend MonoLanguage;
		internal readonly Architecture Architecture;
		protected readonly ProcessServant process;
		MonoSymbolTable symtab;
		Module module;
		string name;

		Hashtable range_hash;
		ArrayList ranges;
		Hashtable type_hash;
		Hashtable class_entry_by_token;
		ArrayList sources;
		Hashtable source_hash;
		Hashtable source_file_hash;
		Hashtable method_index_hash;

		internal MonoSymbolFile (MonoLanguageBackend language, ProcessServant process,
					 TargetMemoryAccess memory, TargetAddress address)
		{
			this.MonoLanguage = language;
			this.TargetMemoryInfo = memory.TargetMemoryInfo;
			this.Architecture = process.Architecture;
			this.process = process;

			ThreadManager = process.ThreadManager;

			int address_size = TargetMemoryInfo.TargetAddressSize;
			int int_size = TargetMemoryInfo.TargetIntegerSize;

			ranges = ArrayList.Synchronized (new ArrayList ());
			range_hash = Hashtable.Synchronized (new Hashtable ());
			type_hash = Hashtable.Synchronized (new Hashtable ());
			class_entry_by_token = Hashtable.Synchronized (new Hashtable ());

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
					"Cannot load symbol file `{0}': {1}", ImageFile, ex);
			}

			ModuleDefinition = Assembly.MainModule;

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			string mdb_file = ImageFile + ".mdb";

			try {
				File = C.MonoSymbolFile.ReadSymbolFile (Assembly, mdb_file);
				if (File == null)
					Report.Error ("Cannot load symbol file `{0}'", mdb_file);
				else if (ModuleDefinition.Mvid != File.Guid) {
					Report.Error ("Symbol file `{0}' does not match assembly `{1}'",
						      mdb_file, ImageFile);
					File = null;
				}
			} catch (C.MonoSymbolFileException ex) {
				Report.Error (ex.Message);
			} catch (Exception ex) {
				Report.Error ("Cannot load symbol file `{0}': {1}", mdb_file, ex);
			}

			symtab = new MonoSymbolTable (this);

			name = Assembly.Name.FullName;

			module = process.Session.GetModule (name);
			if (module == null) {
				module = process.Session.CreateModule (name, this);
			} else {
				module.LoadModule (this);
			}

#if FIXME
			if ((File != null) && (File.OffsetTable.IsAspxSource)) {
				Console.WriteLine ("ASPX SOURCE: {0} {1}", this, File);
			}
#endif

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

		public override string CodeBase {
			get { return ImageFile; }
		}

		public override Language Language {
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
			return range.GetMethod ();
		}

		protected MonoClassType LookupMonoClass (Cecil.TypeReference typeref)
		{
			TargetType type = LookupMonoType (typeref);
			if (type == null)
				return null;

			MonoClassType class_type = type as MonoClassType;
			if (class_type != null)
				return class_type;

			if (type.HasClassType)
				return (MonoClassType) type.ClassType;

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

			bool need_conversion = false;
			if ((Environment.OSVersion.Platform == PlatformID.Unix) &&
			    ((File.OffsetTable.FileFlags & C.OffsetTable.Flags.WindowsFileNames) != 0) &&
			    !process.Session.Config.OpaqueFileNames) {
				need_conversion = true;
			}			

			foreach (C.SourceFileEntry source in File.Sources) {
				string file_name = source.FileName;
				string orig_name = file_name;
				if (need_conversion)
					file_name = DebuggerConfiguration.WindowsToUnix (file_name);

				SourceFile info = new MonoSourceFile (
					process.Session, Module, source, file_name);

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

		internal SourceFile GetSourceFile (int index)
		{
			C.SourceFileEntry source = File != null ? File.GetSourceFile (index) : null;
			if (source == null)
				return null;

			return (SourceFile) source_file_hash [source];
		}

		public MonoFunctionType GetFunctionByToken (int token)
		{
			ensure_sources ();
			Cecil.MethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, token);

			MonoClassType klass = LookupMonoClass (mdef.DeclaringType);
			if (klass == null)
				throw new InternalError ();

			return klass.LookupFunction (mdef);
		}

		MonoMethodSource GetMethodSource (int index)
		{
			ensure_sources ();
			MonoMethodSource method = (MonoMethodSource) method_index_hash [index];
			if (method != null)
				return method;

			if (File == null)
				return null;

			C.MethodEntry entry = File.GetMethod (index);
			SourceFile file = (SourceFile) source_file_hash [entry.CompileUnit.SourceFile];
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

			Cecil.MethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, entry.Token);

			MonoClassType klass = LookupMonoClass (mdef.DeclaringType);
			if (klass == null)
				throw new InternalError ();

			MonoFunctionType function = klass.LookupFunction (mdef);

			MonoMethodSource method = new MonoMethodSource (
				this, file, entry, mdef, klass, function);
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

			List<MethodSource> methods = new List<MethodSource> ();

			foreach (C.MethodEntry method in File.Methods) {
				if (method.CompileUnit.SourceFile.Index == source.Index) {
					methods.Add (GetMethodSource (file, method.Index));
					continue;
				}

				bool found = false;
				foreach (C.SourceFileEntry include in method.CompileUnit.IncludeFiles) {
					if (include.Index == source.Index) {
						found = true;
						break;
					}
				}

				if (found)
					methods.Add (GetMethodSource (file, method.Index));
			}

			return methods.ToArray ();
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
			case "System.Decimal":  return "decimal";
			default:		return RemoveGenericArity (t.FullName);
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
					sb.Append (", ");
				sb.Append (GetTypeSignature (p.ParameterType).Replace ('+','/'));
			}
			sb.Append (")");
			return sb.ToString ();
		}

		internal static string RemoveGenericArity (string name)
		{
			int start = 0;
			StringBuilder sb = null;
			do {
				int pos = name.IndexOf ('`', start);
				if (pos < 0) {
					if (start == 0)
						return name;

					sb.Append (name.Substring (start));
					break;
				}

				if (sb == null)
					sb = new StringBuilder ();
				sb.Append (name.Substring (start, pos-start));

				pos++;
				while ((pos < name.Length) && Char.IsNumber (name [pos]))
					pos++;

				start = pos;
			} while (start < name.Length);

			return sb.ToString ();
		}

		internal static string GetMethodName (Cecil.MethodDefinition mdef)
		{
			StringBuilder sb = new StringBuilder (GetTypeSignature (mdef.DeclaringType));
			if (mdef.DeclaringType.GenericParameters.Count > 0) {
				sb.Append ('<');
				bool first = true;
				foreach (Cecil.GenericParameter p in mdef.DeclaringType.GenericParameters) {
					if (first)
						first = false;
					else
						sb.Append (',');
					sb.Append (p.Name);
				}
				sb.Append ('>');
			}
			sb.Append ('.');
			sb.Append (mdef.Name);
			if (mdef.GenericParameters.Count > 0) {
				sb.Append ('<');
				bool first = true;
				foreach (Cecil.GenericParameter p in mdef.GenericParameters) {
					if (first)
						first = false;
					else
						sb.Append (',');
					sb.Append (p.Name);
				}
				sb.Append ('>');
			}
			sb.Append (GetMethodSignature (mdef));
			return sb.ToString ();
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
			if (File == null)
				return null;
			MonoMethod method = (MonoMethod) method_hash [hash];
			if (method == null) {
				MonoMethodSource source = GetMethodSource (index);
				method = new MonoMethod (
					this, source, hash.Domain, source.Entry, source.Method);
				method_hash.Add (hash, method);
			}

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetMemoryInfo);
				method.Load (reader, TargetMemoryInfo.AddressDomain);
			}

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
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetMemoryInfo);
				method.Load (reader, TargetMemoryInfo.AddressDomain);
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

		const string cgen_attr = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
		const string debugger_display_attr = "System.Diagnostics.DebuggerDisplayAttribute";
		const string type_proxy_attr = "System.Diagnostics.DebuggerTypeProxyAttribute";
		const string browsable_attr = "System.Diagnostics.DebuggerBrowsableAttribute";

		internal static void CheckCustomAttributes (Cecil.ICustomAttributeProvider type,
							    out DebuggerBrowsableState? browsable_state,
							    out DebuggerDisplayAttribute debugger_display,
							    out DebuggerTypeProxyAttribute type_proxy,
							    out bool is_compiler_generated)
		{
			browsable_state = null;
			debugger_display = null;
			type_proxy = null;
			is_compiler_generated = false;

			foreach (Cecil.CustomAttribute cattr in type.CustomAttributes) {
				string cname = cattr.Constructor.DeclaringType.FullName;
				if (cname == cgen_attr) {
					is_compiler_generated = true;
				} else if (cname == debugger_display_attr) {
					string text = (string) cattr.ConstructorParameters [0];
					debugger_display = new DebuggerDisplayAttribute (text);
					foreach (DictionaryEntry prop in cattr.Properties) {
						string key = (string) prop.Key;
						if (key == "Name")
							debugger_display.Name = (string) prop.Value;
						else if (key == "Type")
							debugger_display.Type = (string) prop.Value;
						else {
							debugger_display = null;
							break;
						}
					}
				} else if (cname == browsable_attr) {
					browsable_state = (DebuggerBrowsableState) cattr.Blob [2];
				} else if (cname == type_proxy_attr) {
					string text = (string) cattr.ConstructorParameters [0];
					type_proxy = new DebuggerTypeProxyAttribute (text);
				}
			}
		}

		protected override void DoDispose ()
		{
			if (File != null)
				File.Dispose ();
			base.DoDispose ();
		}

		protected class MonoSourceFile : SourceFile
		{
			C.SourceFileEntry file;

			public MonoSourceFile (DebuggerSession session, Module module,
					       C.SourceFileEntry file, string path)
				: base (session, module, path)
			{
				this.file = file;
			}

			public override bool IsAutoGenerated {
				get {
					return file.AutoGenerated;
				}
			}


			public override bool CheckModified ()
			{
				return !file.CheckChecksum ();
			}
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

				int token = reader.BinaryReader.ReadLeb128 ();
				reader.BinaryReader.ReadLeb128 (); /* instance_size */
				TargetAddress klass_address = reader.ReadAddress ();

				SymbolFile.AddClassEntry (token, klass_address);
			}

			protected override string MyToString ()
			{
				return String.Format (":{0}", SymbolFile);
			}
		}

		protected void AddClassEntry (int token, TargetAddress klass)
		{
			class_entry_by_token.Add (token, new ClassEntry (klass));
		}

		internal MonoClassInfo LookupClassInfo (TargetMemoryAccess target, int token)
		{
			ClassEntry entry = (ClassEntry) class_entry_by_token [token];
			if (entry == null) {
				MonoLanguage.Update (target);
				entry = (ClassEntry) class_entry_by_token [token];
				if (entry == null)
					return null;
			}

			return entry.ReadClassInfo (MonoLanguage, target);
		}

		protected class ClassEntry
		{
			public readonly TargetAddress KlassAddress;

			MonoClassInfo info;

			public ClassEntry (TargetAddress klass)
			{
				this.KlassAddress = klass;
			}

			internal MonoClassInfo ReadClassInfo (MonoLanguageBackend mono,
							      TargetMemoryAccess target)
			{
				if (info == null)
					info = mono.ReadClassInfo (target, KlassAddress);

				return info;
			}
		}

		protected class MonoMethodSource : MethodSource
		{
			protected readonly MonoSymbolFile file;
			protected readonly SourceFile source_file;
			protected readonly C.MethodEntry method;
			protected readonly Cecil.MethodDefinition mdef;
			protected readonly MonoClassType klass;
			public readonly MonoFunctionType function;
			protected readonly string full_name;

			int start_row, end_row;

			public MonoMethodSource (MonoSymbolFile file, SourceFile source_file,
						 C.MethodEntry method, Cecil.MethodDefinition mdef,
						 MonoClassType klass, MonoFunctionType function)
			{
				this.file = file;
				this.source_file = source_file;
				this.method = method;
				this.mdef = mdef;
				this.function = function;
				this.klass = klass;

				full_name = method.GetRealName ();
				if (full_name == null)
					full_name = MonoSymbolFile.GetMethodName (mdef);

				C.LineNumberEntry start, end;
				C.LineNumberTable lnt = method.GetLineNumberTable ();
				if (lnt.GetMethodBounds (out start, out end))
					start_row = start.Row; end_row = end.Row;
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
				get { return start_row; }
			}

			public override int EndRow {
				get { return end_row; }
			}

			internal int Index {
				get { return method.Index; }
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

			public override string[] GetNamespaces ()
			{
				int index = method.NamespaceID;

				Hashtable namespaces = new Hashtable ();

				C.CompileUnitEntry source = method.CompileUnit;
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

		protected class MonoCodeBlock : Block
		{
			List<MonoCodeBlock> children;

			protected MonoCodeBlock (int index, Block.Type type, int start, int end)
				: base (type, index, start, end)
			{ }

			protected void AddChildBlock (MonoCodeBlock child)
			{
				if (children == null)
					children = new List<MonoCodeBlock> ();
				children.Add (child);
			}

			public override Block[] Children {
				get {
					if (children == null)
						return null;

					return children.ToArray ();
				}
			}

			static int find_address (MethodAddress address, int il_offset)
			{
				int num_line_numbers = address.LineNumbers.Count;

				for (int i = 0; i < num_line_numbers; i++) {
					JitLineNumberEntry lne = address.LineNumbers [i];

					if (lne.Offset < 0)
						continue;
					if (lne.Offset >= il_offset)
						return lne.Address;
				}

				return num_line_numbers > 0 ?
					address.LineNumbers [num_line_numbers - 1].Address : 0;
			}

			public static MonoCodeBlock[] CreateBlocks (MonoMethod method,
								    MethodAddress address,
								    C.CodeBlockEntry[] the_blocks,
								    out List<MonoCodeBlock> root_blocks)
			{
				MonoCodeBlock[] blocks = new MonoCodeBlock [the_blocks.Length];
				for (int i = 0; i < blocks.Length; i++) {
					Block.Type type = (Block.Type) the_blocks [i].BlockType;
					int start = find_address (address, the_blocks [i].StartOffset);
					int end = find_address (address, the_blocks [i].EndOffset);
					blocks [i] = new MonoCodeBlock (i, type, start, end);
				}

				root_blocks = new List<MonoCodeBlock> ();

				for (int i = 0; i < blocks.Length; i++) {
					if (the_blocks [i].Parent < 0)
						root_blocks.Add (blocks [i]);
					else {
						MonoCodeBlock parent = blocks [the_blocks [i].Parent - 1];
						blocks [i].Parent = parent;
						parent.AddChildBlock (blocks [i]);
					}
				}

				return blocks;
			}
		}
		
		protected class MonoMethod : Method
		{
			MonoSymbolFile file;
			MethodSource source;
			C.MethodEntry method;
			Cecil.MethodDefinition mdef;
			TargetStructType decl_type;
			TargetVariable this_var;
			MonoCodeBlock[] code_blocks;
			List<MonoCodeBlock> root_blocks;
			List<TargetVariable> parameters;
			List<TargetVariable> locals;
			Dictionary<int,ScopeInfo> scopes;
			bool has_variables;
			bool is_loaded;
			bool is_iterator;
			bool this_is_captured;
			bool is_compiler_generated;
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

				foreach (Cecil.CustomAttribute cattr in mdef.CustomAttributes) {
					string cname = cattr.Constructor.DeclaringType.FullName;
					if ((cname == "System.Diagnostics.DebuggerHiddenAttribute") ||
					    (cname == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
						is_compiler_generated = true;
				}						
			}

			public override object MethodHandle {
				get { return mdef; }
			}

			public override int Domain {

				get { return domain; }
			}

			public override bool IsWrapper {
				get { return false; }
			}

			public override bool IsCompilerGenerated {
				get { return is_compiler_generated; }
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
					file, this, source, method, address.LineNumbers.ToArray ()));
			}

			void do_read_blocks ()
			{
				if (code_blocks != null)
					return;

				C.CodeBlockEntry[] symfile_blocks = method.GetCodeBlocks ();
				code_blocks = MonoCodeBlock.CreateBlocks (
					this, address, symfile_blocks, out root_blocks);

				foreach (C.CodeBlockEntry block in symfile_blocks) {
					if (block.BlockType == C.CodeBlockEntry.Type.IteratorBody)
						is_iterator = true;
				}
			}

			void do_read_variables (TargetMemoryAccess memory)
			{
				if (!is_loaded)
					throw new TargetException (TargetError.MethodNotLoaded);
				if (has_variables)
					return;

				MonoLanguageBackend mono = file.MonoLanguage;

				TargetAddress decl_klass = mono.MonoRuntime.MonoMethodGetClass (
					memory, address.MonoMethod);
				TargetType decl = mono.ReadMonoClass (memory, decl_klass);
				if (decl.HasClassType)
					decl_type = decl.ClassType;
				else
					decl_type = (TargetStructType) decl;

				do_read_blocks ();

				locals = new List<TargetVariable> ();
				parameters = new List<TargetVariable> ();
				scopes = new Dictionary<int,ScopeInfo> ();

				var captured_vars = new Dictionary<string,CapturedVariable> ();

				if (address.HasThis)
					this_var = new MonoVariable (
						"this", decl_type, true, true, this,
						address.ThisVariableInfo);

				var scope_list = new List<ScopeInfo> ();

				C.ScopeVariable[] scope_vars = method.GetScopeVariables ();
				int num_scope_vars = scope_vars != null ? scope_vars.Length : 0;
				for (int i = 0; i < num_scope_vars; i++) {
					C.ScopeVariable sv = scope_vars [i];

					VariableInfo var;
					if (sv.Index < 0) {
						var = address.ThisVariableInfo;
						this_is_captured = true;
						this_var = null;
					} else
						var = address.LocalVariableInfo [sv.Index];

					try {
						TargetStructType type = mono.ReadStructType (memory, var.MonoType);
						MonoVariable scope_var = new MonoVariable (
							"$__" + sv.Scope, type, true, type.IsByRef, this, var);

						ScopeInfo info = new ScopeInfo (sv.Scope, scope_var, type);
						scopes.Add (sv.Scope, info);
						scope_list.Add (info);
					} catch (Exception ex) {
						Report.Error ("Cannot read scope variable: {0}\n{1}", var, ex);
					}
				}

				foreach (ScopeInfo scope in scope_list) {
					read_scope (scope);
				}

				foreach (ScopeInfo scope in scopes.Values) {
					C.AnonymousScopeEntry entry = file.File.GetAnonymousScope (scope.ID);
					foreach (C.CapturedVariable captured in entry.CapturedVariables) {
						CapturedVariable cv = new CapturedVariable (
							scope, this, captured.Name, captured.CapturedName);

						switch (captured.Kind) {
						case C.CapturedVariable.CapturedKind.Local:
							locals.Add (cv);
							break;
						case C.CapturedVariable.CapturedKind.Parameter:
							parameters.Add (cv);
							break;
						case C.CapturedVariable.CapturedKind.This:
							if (!cv.Resolve (memory))
								throw new InternalError ();
							if (cv.Type.HasClassType)
								decl_type = cv.Type.ClassType;
							else
								decl_type = (TargetStructType) cv.Type;
							this_var = cv;
							continue;
						default:
							throw new InternalError ();
						}

						captured_vars.Add (captured.Name, cv);
					}
				}

				Cecil.ParameterDefinitionCollection param_info = mdef.Parameters;
				for (int i = 0; i < param_info.Count; i++) {
					if (captured_vars.ContainsKey (param_info [i].Name))
						continue;

					VariableInfo var = address.ParamVariableInfo [i];
					TargetType type = mono.ReadType (memory, var.MonoType);
					if (type == null)
						type = mono.VoidType;

					parameters.Add (new MonoVariable (
						param_info [i].Name, type, false, type.IsByRef,
						this, var, 0, 0));
				}

				C.LocalVariableEntry[] symfile_locals = method.GetLocals ();
				for (int i = 0; i < symfile_locals.Length; i++) {
					C.LocalVariableEntry local = symfile_locals [i];

					if (captured_vars.ContainsKey (local.Name))
						continue;

					VariableInfo var = address.LocalVariableInfo [local.Index];
					TargetType type = mono.ReadType (memory, var.MonoType);
					if (type == null)
						type = mono.VoidType;

					if (local.BlockIndex > 0) {
						int index = local.BlockIndex - 1;
						MonoCodeBlock block = code_blocks [index];
						locals.Add (new MonoVariable (
							local.Name, type, true, type.IsByRef, this, var,
							block.StartAddress, block.EndAddress));
					} else {
						locals.Add (new MonoVariable (
							local.Name, type, true, type.IsByRef, this, var));
					}
				}

				has_variables = true;
			}

			void read_variables (Thread thread)
			{
				if (!is_loaded)
					throw new TargetException (TargetError.MethodNotLoaded);
				if (has_variables)
					return;

				thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess target)  {
						do_read_variables (target);
						return null;
				});
			}

			void read_scope (ScopeInfo scope)
			{
				C.AnonymousScopeEntry entry = file.File.GetAnonymousScope (scope.ID);
				foreach (C.CapturedScope captured in entry.CapturedScopes) {
					if (scopes.ContainsKey (captured.Scope))
						continue;

					CapturedVariable pvar = new CapturedVariable (
						scope, this, captured.CapturedName, captured.CapturedName);
					ScopeInfo child = new ScopeInfo (captured.Scope, pvar);

					scopes.Add (captured.Scope, child);
					read_scope (child);
				}
			}

			void dump_blocks (Block[] blocks, string ident)
			{
				foreach (MonoCodeBlock block in blocks) {
					Console.WriteLine ("{0} {1}", ident, block);
					if (block.Children != null)
						dump_blocks (block.Children, ident + "  ");
				}
			}

			Block lookup_block (TargetAddress address, Block[] blocks)
			{
				foreach (MonoCodeBlock block in blocks) {
					if ((address < StartAddress + block.StartAddress) ||
					    (address >= StartAddress + block.EndAddress))
						continue;

					if (block.Children != null) {
						Block child = lookup_block (address, block.Children);
						return child ?? block;
					}

					return block;
				}

				return null;
			}

			internal override Block LookupBlock (TargetMemoryAccess memory,
							     TargetAddress address)
			{
				do_read_variables (memory);
				return lookup_block (address, root_blocks.ToArray ());
			}

			internal override bool IsIterator {
				get {
					do_read_blocks ();
					return is_iterator;
				}
			}

			public override TargetVariable[] GetParameters (Thread target)
			{
				read_variables (target);
				return parameters.ToArray ();
			}

			public override TargetVariable[] GetLocalVariables (Thread target)
			{
				read_variables (target);
				return locals.ToArray ();
			}

			public override TargetStructType GetDeclaringType (Thread target)
			{
				read_variables (target);
				return decl_type;
			}

			public override bool HasThis {
				get {
					if (this_is_captured)
						return this_var != null;
					else
						return !mdef.IsStatic;
				}
			}

			public override TargetVariable GetThis (Thread target)
			{
				read_variables (target);
				return this_var;
			}

			internal override MethodSource GetTrampoline (TargetMemoryAccess memory,
								      TargetAddress address)
			{
#if FIXME
				return file.MonoLanguage.GetTrampoline (memory, address);
#else
				return null;
#endif
			}

			public override string[] GetNamespaces ()
			{
				int index = method.NamespaceID;

				Hashtable namespaces = new Hashtable ();

				C.CompileUnitEntry source = method.CompileUnit;
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
			public readonly MonoSymbolFile File;
			public readonly Method Method;

			TargetAddress start, end;

			protected MonoLineNumberTable (MonoSymbolFile file, Method method)
			{
				this.File = file;
				this.Method = method;

				this.start = method.StartAddress;
				this.end = method.EndAddress;
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

			public override bool HasMethodBounds {
				get {
					return Data.Addresses.Length > 0;
				}
			}

			public override TargetAddress MethodStartAddress {
				get {
					if (!HasMethodBounds)
						throw new InvalidOperationException ();

					return Data.Addresses [0].Address;
				}
			}

			public override TargetAddress MethodEndAddress {
				get {
					if (!HasMethodBounds)
						throw new InvalidOperationException ();

					return Method.HasMethodBounds ?
						Method.MethodEndAddress : Method.EndAddress;
				}
			}

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

				if (Addresses.Length < 1)
					return null;

				TargetAddress next_address = end;
				TargetAddress next_not_hidden = end;

				for (int i = Addresses.Length-1; i >= 0; i--) {
					LineEntry entry = (LineEntry) Addresses [i];

					int range = (int) (next_not_hidden - address);

					next_address = entry.Address;
					if (!entry.IsHidden)
						next_not_hidden = entry.Address;

					if (next_address > address)
						continue;

					int offset = (int) (address - next_address);
					return create_address (entry, offset, range);
				}

				if (Addresses.Length < 1)
					return null;

				return create_address (Addresses [0], (int) (address - start),
						       (int) (Addresses [0].Address - address));
			}

			SourceAddress create_address (LineEntry entry, int offset, int range)
			{
				SourceFile file = null;
				SourceBuffer buffer = null;

				if (entry.File != 0) {
					file = File.GetSourceFile (entry.File);
				} else {
					if (Method.MethodSource.HasSourceFile)
						file = Method.MethodSource.SourceFile;
					if (Method.MethodSource.HasSourceBuffer)
						buffer = Method.MethodSource.SourceBuffer;
				}

				return new SourceAddress (file, buffer, entry.Line, offset, range);
			}

			public override void DumpLineNumbers ()
			{
				Console.WriteLine ();
				Console.Write ("Dumping Line Number Table: {0} - {1} {2}",
					       Method.Name, Method.StartAddress, Method.EndAddress);
				if (Method.HasMethodBounds)
					Console.Write (" - {0} {1}", Method.MethodStartAddress,
						       Method.MethodEndAddress);
				Console.WriteLine ();
				Console.WriteLine ();

				Console.WriteLine ("Generated Lines (file / line / address):");
				Console.WriteLine ("----------------------------------------");

				for (int i = 0; i < Addresses.Length; i++) {
					LineEntry entry = (LineEntry) Addresses [i];
					Console.WriteLine ("{0,4} {1,4} {2,4}  {3}", i,
							   entry.File, entry.Line, entry.Address);
				}

				Console.WriteLine ("----------------------------------------");
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
			JitLineNumberEntry[] line_numbers;
			C.MethodEntry entry;
			Method method;
			Hashtable namespaces;

			public MonoMethodLineNumberTable (MonoSymbolFile file, Method method,
							  MethodSource source, C.MethodEntry entry,
							  JitLineNumberEntry[] jit_lnt)
				: base (file, method)
			{
				this.method = method;
				this.entry = entry;
				this.line_numbers = jit_lnt;
			}

			void generate_line_number (List<LineEntry> lines, TargetAddress address,
						   int offset, ref int last_line)
			{
				C.LineNumberTable lnt = entry.GetLineNumberTable ();
				C.LineNumberEntry[] line_numbers = lnt.LineNumbers;

				for (int i = line_numbers.Length - 1; i >= 0; i--) {
					C.LineNumberEntry lne = line_numbers [i];

					if (lne.Offset > offset)
						continue;

					if (lne.Row != last_line) {
						int file = lne.File != entry.CompileUnit.SourceFile.Index ? lne.File : 0;
						bool hidden = lne.IsHidden;

						lines.Add (new LineEntry (address, file, lne.Row, hidden));
						last_line = lne.Row;
					}

					break;
				}
			}

			protected override LineNumberTableData ReadLineNumbers ()
			{
				List<LineEntry> lines = new List<LineEntry> ();
				int last_line = -1;

				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					generate_line_number (lines, method.StartAddress + lne.Address,
							      lne.Offset, ref last_line);
				}

				lines.Sort ();

				int start_row = 0, end_row = 0;
				if (lines.Count > 0) {
					start_row = lines [0].Line;
					end_row = lines [0].Line;

					foreach (LineEntry line in lines) {
						if (line.IsHidden || (line.File != 0))
							continue;

						if (line.Line < start_row)
							start_row = line.Line;
						if (line.Line > end_row)
							end_row = line.Line;
					}
				}

				return new LineNumberTableData (start_row, end_row, lines.ToArray ());
			}

			public override void DumpLineNumbers ()
			{
				base.DumpLineNumbers ();

				Console.WriteLine ();
				Console.WriteLine ("Symfile Line Numbers (file / row / offset):");
				Console.WriteLine ("-------------------------------------------");

				C.LineNumberEntry[] lnt;
				lnt = entry.GetLineNumberTable ().LineNumbers;
				for (int i = 0; i < lnt.Length; i++) {
					C.LineNumberEntry lne = lnt [i];

					bool hidden = lne.IsHidden;
					int file = lne.File;

					Console.WriteLine ("{0,4} {1,4} {2,4} {3,4:x}{4}", i,
							   file, lne.Row, lne.Offset,
							   hidden ? " (hidden)" : "");
				}

				Console.WriteLine ("-------------------------------------------");

				Console.WriteLine ();
				Console.WriteLine ("JIT Line Numbers (il / native / address):");
				Console.WriteLine ("-----------------------------------------");
				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					Console.WriteLine ("{0,4} {1,4:x} {2,4:x}  {3,4:x}", i, lne.Offset,
							   lne.Address, method.StartAddress + lne.Address);
				}
				Console.WriteLine ("-----------------------------------------");
			}
		}

		protected struct MethodHashEntry
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

				WrapperEntry wrapper = null;

				if (!wrapper_data.IsNull) {
					int wrapper_size = 4 + 3 * memory.TargetMemoryInfo.TargetAddressSize;

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

		protected class WrapperEntry
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

			public override string[] GetNamespaces ()
			{
				return null;
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

			public override bool IsWrapper {
				get { return true; }
			}

			public override bool IsCompilerGenerated {
				get { return false; }
			}

			public override bool HasSource {
				get { return true; }
			}

			public override MethodSource MethodSource {
				get { return source; }
			}

			public override TargetStructType GetDeclaringType (Thread target)
			{
				return null;
			}

			public override bool HasThis {
				get { return false; }
			}

			public override TargetVariable GetThis (Thread target)
			{
				throw new InvalidOperationException ();
			}

			public override TargetVariable[] GetParameters (Thread target)
			{
				return new TargetVariable [0];
			}

			public override TargetVariable[] GetLocalVariables (Thread target)
			{
				return new TargetVariable [0];
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
				return File.MonoLanguage.GetTrampoline (memory, address);
			}
		}

		protected class WrapperLineNumberTable : MonoLineNumberTable
		{
			WrapperMethod wrapper;
			MethodAddress address;

			public WrapperLineNumberTable (WrapperMethod wrapper, MethodAddress address)
				: base (wrapper.File, wrapper)
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
						lines.Add (new LineEntry (address, 0, i + 1));
						last_line = i + 1;
					}

					break;
				}
			}

			protected override LineNumberTableData ReadLineNumbers ()
			{
				ArrayList lines = new ArrayList ();
				int last_line = -1;

				JitLineNumberEntry[] line_numbers = address.LineNumbers.ToArray ();

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

				lines.Add (new LineEntry (wrapper.StartAddress, 0, 1));

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
