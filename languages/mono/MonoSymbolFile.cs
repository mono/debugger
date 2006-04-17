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

		internal enum AddressMode : long
		{
			Register	= 0,
			RegOffset	= 0x10000000,
			TwoRegisters	= 0x20000000
		}

		const long AddressModeFlags = 0xf0000000;

		public static int StructSize {
			get { return 20; }
		}

		public VariableInfo (Architecture arch, TargetBinaryReader reader)
		{
			Index = reader.ReadLeb128 ();
			Offset = reader.ReadSLeb128 ();
			Size = reader.ReadLeb128 ();
			BeginLiveness = reader.ReadLeb128 ();
			EndLiveness = reader.ReadLeb128 ();

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
			return String.Format ("[VariableInfo {0}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}]",
					      Mode, Index, Offset, Size, BeginLiveness, EndLiveness);
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

		public MethodAddress (C.MethodEntry entry, TargetBinaryReader reader,
				      AddressDomain domain, Architecture arch)
		{
			// here we read the MonoDebugMethodAddress structure
			// as written out in mono_debug_add_method.
			reader.Position = 16;
			int code_size = reader.ReadInt32 ();
			reader.Position += 4;
			StartAddress = ReadAddress (reader, domain);
			EndAddress = StartAddress + code_size;
			WrapperAddress = ReadAddress (reader, domain);
			ReadAddress (reader, domain);

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

	internal class MonoSymbolFile : SymbolFile, IDisposable
	{
		internal readonly Module Module;
		internal readonly int Index;
		internal readonly Cecil.IAssemblyDefinition Assembly;
		internal readonly Cecil.IModuleDefinition ModuleDefinition;
		internal readonly TargetAddress MonoImage;
		internal readonly string ImageFile;
		internal readonly C.MonoSymbolFile File;
		internal readonly ThreadManager ThreadManager;
		internal readonly AddressDomain AddressDomain;
		internal readonly ITargetMemoryInfo TargetInfo;
		internal readonly MonoLanguageBackend MonoLanguage;
		protected readonly Process process;
		MonoSymbolTable symtab;
		string name;
		int address_size;
		int int_size;

		Hashtable range_hash;
		ArrayList ranges;
		ArrayList wrappers;
		Hashtable type_hash;
		Hashtable class_entry_hash;
		ArrayList sources;
		Hashtable source_hash;
		Hashtable source_file_hash;
		Hashtable method_index_hash;

		internal MonoSymbolFile (MonoLanguageBackend language, Process process,
					 ITargetMemoryInfo target_info, ITargetMemoryAccess memory,
					 TargetAddress address)
		{
			this.MonoLanguage = language;
			this.TargetInfo = target_info;
			this.process = process;

			ThreadManager = process.ThreadManager;
			AddressDomain = memory.AddressDomain;

			address_size = TargetInfo.TargetAddressSize;
			int_size = TargetInfo.TargetIntegerSize;

			ranges = new ArrayList ();
			wrappers = new ArrayList ();
			range_hash = new Hashtable ();
			type_hash = new Hashtable ();
			class_entry_hash = new Hashtable ();

			Index = memory.ReadInteger (address);
			address += int_size;
			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);
			MonoImage = memory.ReadAddress (address);
			address += address_size;

			Assembly = Cecil.AssemblyFactory.GetAssembly (ImageFile);
			ModuleDefinition = Assembly.MainModule;

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			try {
				System.Reflection.Assembly ass = System.Reflection.Assembly.LoadFrom (ImageFile);
				File = C.MonoSymbolFile.ReadSymbolFile (ass);
			} catch (Exception ex) {
				Console.WriteLine (ex.Message);
			}

			symtab = new MonoSymbolTable (this);

			name = Assembly.Name.FullName;

			Module = process.ModuleManager.GetModule (name);
			if (Module == null) {
				Module = new Module (name, this);
				process.ModuleManager.AddModule (Module);
			} else {
				Module.LoadModule (this);
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})",
					      GetType (), ImageFile, Module);
		}

		protected ArrayList SymbolRanges {
			get { return ranges; }
		}

		protected ArrayList WrapperEntries {
			get { return wrappers; }
		}

		public override ISymbolTable SymbolTable {
			get { return symtab; }
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

		internal Architecture Architecture {
			get { return TargetInfo.Architecture; }
		}

		public override bool SymbolsLoaded {
			get { return Module.LoadSymbols; }
		}

		public override SourceFile[] Sources {
			get { return GetSources (); }
		}

		public override bool HasDebuggingInfo {
			get { return File != null; }
		}

		internal void AddRangeEntry (TargetReader reader, byte[] contents)
		{
			MethodRangeEntry range = MethodRangeEntry.Create (this, reader, contents);
			if (!range_hash.Contains (range.Index)) {
				range_hash.Add (range.Index, range);
				ranges.Add (range);
			}
		}

		internal void AddWrapperEntry (ITargetMemoryAccess memory, TargetReader reader,
					       byte[] contents)
		{
			WrapperEntry wrapper = WrapperEntry.Create (this, memory, reader, contents);
			wrappers.Add (wrapper);
		}

		internal void AddClassEntry (TargetReader reader, byte[] contents)
		{
			ClassEntry entry = new ClassEntry (this, reader, contents);
			class_entry_hash.Add (new TypeHashEntry (entry), entry);
		}

		public TargetType LookupMonoType (Cecil.ITypeReference type)
		{
			TargetType result = (TargetType) type_hash [type];
			if (result != null)
				return result;

			if (type is Cecil.IArrayType) {
				Cecil.IArrayType atype = (Cecil.IArrayType) type;
				TargetType element_type = LookupMonoType (atype.ElementType);
				result = new MonoArrayType (element_type, atype.Rank);
			} else if (type is Cecil.ITypeDefinition) {
				Cecil.ITypeDefinition tdef = (Cecil.ITypeDefinition) type;
				if (tdef.IsEnum)
					result = new MonoEnumType (this, tdef);
				else
					result = new MonoClassType (this, tdef);
			} else {
				Console.WriteLine ("UNKNOWN TYPE: {0} {1}", type, type.GetType ());
				return null;
			}

			type_hash.Add (type, result);
			return result;
		}

		public void AddType (TargetType type, Cecil.ITypeDefinition typedef)
		{
			type_hash.Add (typedef, type);
		}

		public TargetBinaryReader GetTypeInfo (Cecil.ITypeDefinition type)
		{
			ClassEntry entry = (ClassEntry) class_entry_hash [new TypeHashEntry (type)];
			if (entry == null)
				return null;
			return entry.Contents;
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
			foreach (MethodRangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address > range.EndAddress))
					continue;

				long offset = address - range.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				Method method = range.GetMethod ();
				return new Symbol (
					method.Name, range.StartAddress, (int) offset);
			}

			foreach (WrapperEntry wrapper in wrappers) {
				if ((address < wrapper.StartAddress) || (address > wrapper.EndAddress))
					continue;

				long offset = address - wrapper.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				return new Symbol (
					wrapper.Name, wrapper.StartAddress, (int) offset);
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

		SourceMethod GetSourceMethod (int index)
		{
			ensure_sources ();
			SourceMethod method = (SourceMethod) method_index_hash [index];
			if (method != null)
				return method;

			C.MethodEntry entry = File.GetMethod (index);
			SourceFile file = (SourceFile) source_file_hash [entry.SourceFile];

			return CreateSourceMethod (file, index);
		}

		SourceMethod GetSourceMethod (SourceFile file, int index)
		{
			ensure_sources ();
			SourceMethod method = (SourceMethod) method_index_hash [index];
			if (method != null)
				return method;

			return CreateSourceMethod (file, index);
		}

		SourceMethod CreateSourceMethod (SourceFile file, int index)
		{
			C.MethodEntry entry = File.GetMethod (index);
			C.MethodSourceEntry source = File.GetMethodSource (index);

			Cecil.IMethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, entry.Token);

			StringBuilder sb = new StringBuilder (mdef.DeclaringType.FullName);
			sb.Append (".");
			sb.Append (mdef.Name);
			sb.Append ("(");
			bool first = true;
			foreach (Cecil.IParameterReference param in mdef.Parameters) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (param.ParameterType.FullName);
			}
			sb.Append (")");

			string name = sb.ToString ();
			SourceMethod method = new SourceMethod (
				Module, file, source.Index, name, source.StartRow,
				source.EndRow, true);

			method_index_hash.Add (index, method);
			return method;
		}

		public SourceMethod GetMethod (int index)
		{
			return GetSourceMethod (index);
		}

		public SourceMethod GetMethodByToken (int token)
		{
			if (File == null)
				return null;

			ensure_sources ();
			C.MethodEntry entry = File.GetMethodByToken (token);
			if (entry == null)
				return null;
			return GetSourceMethod (entry.Index);
		}

		Hashtable method_hash = new Hashtable ();

		public override Method GetMethod (int domain, long handle)
		{
			MethodHashEntry index = new MethodHashEntry (domain, (int) handle);
			MethodRangeEntry entry = (MethodRangeEntry) range_hash [index];
			if (entry == null)
				return null;

			return entry.GetMethod ();
		}

		public override SourceMethod[] GetMethods (SourceFile file)
		{
			ensure_sources ();
			C.SourceFileEntry source = (C.SourceFileEntry) source_hash [file];

			C.MethodSourceEntry[] entries = source.Methods;
			SourceMethod[] methods = new SourceMethod [entries.Length];

			for (int i = 0; i < entries.Length; i++)
				methods [i] = GetSourceMethod (file, entries [i].Index);

			return methods;
		}

		public override SourceMethod FindMethod (string name)
		{
			return null;
		}

		protected MonoMethod GetMonoMethod (MethodHashEntry index)
		{
			ensure_sources ();
			MonoMethod mono_method = (MonoMethod) method_hash [index];
			if (mono_method != null)
				return mono_method;

			SourceMethod method = GetSourceMethod (index.Index);
			C.MethodEntry entry = File.GetMethod (index.Index);

			Cecil.IMethodDefinition mdef = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, entry.Token);

			mono_method = new MonoMethod (this, method, entry, mdef);
			method_hash.Add (index, mono_method);
			return mono_method;
		}

		protected MonoMethod GetMonoMethod (MethodHashEntry index, byte[] contents)
		{
			MonoMethod method = GetMonoMethod (index);

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetInfo);
				method.Load (reader, AddressDomain);
			}

			return method;
		}

		internal MonoFunctionType GetFunctionType (string class_name, int token)
		{
			MonoClassType klass = null;

			Cecil.ITypeDefinitionCollection types = Assembly.MainModule.Types;
			// FIXME: Work around an API problem in Cecil.
			foreach (Cecil.ITypeDefinition type in types) {
				if (type.FullName != class_name)
					continue;

				klass = LookupMonoType (type) as MonoClassType;
				break;
			}

			if (klass == null)
				return null;

			Cecil.IMethodDefinition minfo = MonoDebuggerSupport.GetMethod (
				ModuleDefinition, token);

			StringBuilder sb = new StringBuilder ();
			bool first = true;
			foreach (Cecil.IParameterReference pinfo in minfo.Parameters) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (pinfo.ParameterType);
			}

			string fname = String.Format (
				"{0}.{1}({2})", klass.Name, minfo.Name, sb.ToString ());

			return new MonoFunctionType (klass, minfo, fname);
		}

		internal override ILoadHandler RegisterLoadHandler (Thread target,
								    SourceMethod source,
								    MethodLoadedHandler handler,
								    object user_data)
		{
			int index = (int) source.Handle;
			MonoMethod method = GetMonoMethod (new MethodHashEntry (0, index));
			return method.RegisterLoadHandler (target, handler, user_data);
		}

		internal override StackFrame UnwindStack (StackFrame last_frame,
							  ITargetMemoryAccess memory)
		{
			return null;
		}

		internal override void OnModuleChanged ()
		{ }

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (File != null)
						File.Dispose ();
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

		~MonoSymbolFile ()
		{
			Dispose (false);
		}

		protected class MonoMethod : Method
		{
			MonoSymbolFile file;
			SourceMethod info;
			C.MethodEntry method;
			Cecil.IMethodDefinition mdef;
			MonoClassType decl_type;
			TargetType[] param_types;
			TargetType[] local_types;
			TargetVariable this_var;
			TargetVariable[] parameters;
			TargetVariable[] locals;
			bool has_variables;
			bool is_loaded;
			MethodAddress address;
			Hashtable load_handlers;

			public MonoMethod (MonoSymbolFile file, SourceMethod info,
					   C.MethodEntry method, Cecil.IMethodDefinition mdef)
				: base (info.Name, file.ImageFile, file.Module)
			{
				this.file = file;
				this.info = info;
				this.method = method;
				this.mdef = mdef;
			}

			public override object MethodHandle {
				get { return mdef; }
			}

			public void Load (TargetBinaryReader dynamic_reader, AddressDomain domain)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (
					method, dynamic_reader, domain, file.Architecture);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);

				SetSource (new MonoMethodSource (file.MonoLanguage.SourceFileFactory, this, info, method, address.LineNumbers));
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				Cecil.IParameterDefinitionCollection param_info = mdef.Parameters;
				param_types = new TargetType [param_info.Count];
				parameters = new TargetVariable [param_info.Count];
				for (int i = 0; i < param_info.Count; i++) {
					Cecil.ITypeReference type = param_info [i].ParameterType;

					param_types [i] = file.MonoLanguage.LookupMonoType (type);
					if (param_types [i] == null)
						param_types [i] = file.MonoLanguage.VoidType;

					parameters [i] = new MonoVariable (
						file.process, param_info [i].Name, param_types [i],
						false, param_types [i].IsByRef, this,
						address.ParamVariableInfo [i], 0, 0);
				}

				local_types = new TargetType [method.NumLocals];
				locals = new TargetVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					C.LocalVariableEntry local = method.Locals [i];
					local_types [i] = MonoDebuggerSupport.GetLocalTypeFromSignature (
						file, local.Signature);

					if (local.BlockIndex > 0) {
						int index = local.BlockIndex - 1;
						JitLexicalBlockEntry block = address.LexicalBlocks [index];
						locals [i] = new MonoVariable (
							file.process, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [local.Index],
							block.StartAddress, block.EndAddress);
					} else {
						locals [i] = new MonoVariable (
							file.process, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [local.Index]);
					}
				}

				decl_type = (MonoClassType) file.MonoLanguage.LookupMonoType (mdef.DeclaringType);

				if (address.HasThis)
					this_var = new MonoVariable (
						file.process, "this", decl_type, true,
						true, this, address.ThisVariableInfo);

				has_variables = true;
			}

			public override TargetVariable[] Parameters {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return parameters;
				}
			}

			public override TargetVariable[] Locals {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return locals;
				}
			}

			public override TargetClassType DeclaringType {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return decl_type;
				}
			}

			public override bool HasThis {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return this_var != null;
				}
			}

			public override TargetVariable This {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return this_var;
				}
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory,
								    TargetAddress address)
			{
				return file.LanguageBackend.GetTrampoline (memory, address);
			}

			void breakpoint_hit (Inferior inferior, TargetAddress address,
					     object user_data)
			{
				if (load_handlers == null)
					return;

				// ensure_method ();

				foreach (HandlerData handler in load_handlers.Keys)
					handler.Handler (inferior, info, handler.UserData);

				load_handlers = null;
			}

			// This must match mono_type_get_desc() in mono/metadata/debug-helpers.c.
			string GetTypeSignature (Cecil.ITypeReference t)
			{
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
				case "System.Object":
				default:		return t.FullName;
				}
			}

#region load handlers for unjitted methods
			public ILoadHandler RegisterLoadHandler (Thread target,
								 MethodLoadedHandler handler,
								 object user_data)
			{
				StringBuilder sb = new StringBuilder ();
				sb.Append (mdef.DeclaringType.FullName);
				sb.Append (":");
				sb.Append (mdef.Name);
				sb.Append ("(");
				for (int i = 0; i < mdef.Parameters.Count; i++) {
					if (i > 0)
						sb.Append (",");
					sb.Append (GetTypeSignature (mdef.Parameters[i].ParameterType).Replace ('+','/'));
				}
				sb.Append (")");
				string full_name = sb.ToString ();

				if (load_handlers == null) {
					/* only insert the load handler breakpoint once */
					file.MonoLanguage.InsertBreakpoint (
						target, full_name,
						new BreakpointHandler (breakpoint_hit),
						null);
				 
					load_handlers = new Hashtable ();
				}

				/* but permit lots of handlers so we
				 * can insert multiple breakpoints in
				 * an unjitted method */
				HandlerData data = new HandlerData (this, handler, user_data);

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

			protected sealed class HandlerData : MarshalByRefObject, ILoadHandler
			{
				public readonly MonoMethod Method;
				public readonly MethodLoadedHandler Handler;
				public readonly object UserData;

				public HandlerData (MonoMethod method,
						    MethodLoadedHandler handler,
						    object user_data)
				{
					this.Method = method;
					this.Handler = handler;
					this.UserData = user_data;
				}

				object ILoadHandler.UserData {
					get { return UserData; }
				}

				public void Remove ()
				{
					Method.UnRegisterLoadHandler (this);
				}
			}
#endregion
		}

		protected class MonoMethodSource : MethodSource
		{
			int start_row, end_row;
			JitLineNumberEntry[] line_numbers;
			C.MethodEntry method;
			SourceMethod source_method;
			Method imethod;
			SourceFileFactory factory;
			Hashtable namespaces;

			public MonoMethodSource (SourceFileFactory factory, Method imethod,
						 SourceMethod source_method, C.MethodEntry method,
						 JitLineNumberEntry[] line_numbers)
				: base (imethod, source_method.SourceFile)
			{
				this.factory = factory;
				this.imethod = imethod;
				this.method = method;
				this.line_numbers = line_numbers;
				this.source_method = source_method;
				this.start_row = method.StartRow;
				this.end_row = method.EndRow;
			}

			void generate_line_number (ArrayList lines, TargetAddress address, int offset,
						   ref int last_line)
			{
				for (int i = method.NumLineNumbers - 1; i >= 0; i--) {
					C.LineNumberEntry lne = method.LineNumbers [i];

					if (lne.Offset > offset)
						continue;

					if (lne.Row != last_line) {
						lines.Add (new LineEntry (address, lne.Row));
						last_line = lne.Row;
					}

					break;
				}
			}

			protected override MethodSourceData ReadSource ()
			{
				ArrayList lines = new ArrayList ();
				int last_line = -1;

				for (int i = 0; i < line_numbers.Length; i++) {
					JitLineNumberEntry lne = line_numbers [i];

					generate_line_number (lines, imethod.StartAddress + lne.Address,
							      lne.Offset, ref last_line);
				}

				lines.Sort ();

				LineEntry[] addresses = new LineEntry [lines.Count];
				lines.CopyTo (addresses, 0);

				ISourceBuffer buffer = factory.FindFile (source_method.SourceFile.FileName);
				return new MethodSourceData (
					start_row, end_row, addresses, source_method, buffer,
					source_method.SourceFile.Module);
			}

			public override string[] GetNamespaces ()
			{
				int index = method.NamespaceID;

				if (namespaces == null) {
					namespaces = new Hashtable ();

					C.SourceFileEntry source = method.SourceFile;
					foreach (C.NamespaceEntry entry in source.Namespaces)
						namespaces.Add (entry.Index, entry);
				}

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

		private struct MethodHashEntry
		{
			public readonly int Domain;
			public readonly int Index;

			public MethodHashEntry (int domain, int index)
			{
				this.Domain = domain;
				this.Index = index;
			}

			public override string ToString ()
			{
				return String.Format ("MethodHashEntry ({0}:{1:x})", Domain, Index);
			}
		}

		private class MethodRangeEntry : SymbolRangeEntry
		{
			public readonly MonoSymbolFile File;
			public readonly MethodHashEntry Index;
			public readonly TargetAddress WrapperAddress;
			readonly byte[] contents;

			private MethodRangeEntry (MonoSymbolFile file, int domain, int index,
						  TargetAddress start_address, TargetAddress end_address,
						  TargetAddress wrapper_address, byte[] contents)
				: base (start_address, end_address)
			{
				this.File = file;
				this.Index = new MethodHashEntry (domain, index);
				this.WrapperAddress = wrapper_address;
				this.contents = contents;
			}

			public static MethodRangeEntry Create (MonoSymbolFile file, TargetReader reader,
							       byte[] contents)
			{
				int domain = reader.BinaryReader.ReadInt32 ();
				int index = reader.BinaryReader.ReadInt32 ();
				int size = reader.BinaryReader.ReadInt32 ();
				reader.BinaryReader.ReadInt32 (); /* dummy */
				TargetAddress start = reader.ReadAddress ();
				TargetAddress end = start + size;
				TargetAddress wrapper = reader.ReadAddress ();

				return new MethodRangeEntry (
					file, domain, index, start, end, wrapper, contents);
			}

			internal Method GetMethod ()
			{
				return File.GetMonoMethod (Index, contents);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return File.GetMonoMethod (Index, contents);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry [{3}:{0:x}:{1:x}:{2:x}]",
						      StartAddress, EndAddress, Index, File);
			}
		}

		private class WrapperEntry : SymbolRangeEntry
		{
			public readonly MonoSymbolFile File;
			public readonly TargetAddress WrapperMethod;
			public readonly TargetAddress MethodStartAddress;
			public readonly TargetAddress MethodEndAddress;
			public readonly WrapperType WrapperType;
			public readonly string Name;
			public readonly string CILCode;
			public readonly JitLineNumberEntry[] LineNumbers;
			WrapperMethod method;

			private WrapperEntry (MonoSymbolFile file, TargetAddress method, string name,
					      TargetAddress code_start, int code_size,
					      TargetAddress prologue_end, TargetAddress epilogue_begin,
					      WrapperType wrapper_type, string cil_code,
					      JitLineNumberEntry[] line_numbers)
				: base (code_start, code_start + code_size)
			{
				this.File = file;
				this.WrapperMethod = method;
				this.MethodStartAddress = prologue_end;
				this.MethodEndAddress = epilogue_begin;
				this.WrapperType = wrapper_type;
				this.Name = name;
				this.CILCode = cil_code;
				this.LineNumbers = line_numbers;
			}

			public static WrapperEntry Create (MonoSymbolFile file, ITargetMemoryAccess memory,
							   TargetReader reader, byte[] contents)
			{
				int size = reader.BinaryReader.ReadInt32 ();
				TargetAddress wrapper = reader.ReadAddress ();
				TargetAddress code = reader.ReadAddress ();

				TargetAddress name_address = reader.ReadAddress ();
				TargetAddress cil_address = reader.ReadAddress ();

				string name = "<" + memory.ReadString (name_address) + ">";
				string cil_code = memory.ReadString (cil_address);

				TargetAddress prologue_end = code + reader.BinaryReader.ReadLeb128 ();
				TargetAddress epilogue_begin = code + reader.BinaryReader.ReadLeb128 ();

				int num_line_numbers = reader.BinaryReader.ReadLeb128 ();
				JitLineNumberEntry[] lines = new JitLineNumberEntry [num_line_numbers];

				int il_offset = 0, native_offset = 0;
				for (int i = 0; i < num_line_numbers; i++) {
					il_offset += reader.BinaryReader.ReadSLeb128 ();
					native_offset += reader.BinaryReader.ReadSLeb128 ();

					lines [i] = new JitLineNumberEntry (il_offset, native_offset);
				}

				int wrapper_type = reader.BinaryReader.ReadLeb128 ();

				return new WrapperEntry (
					file, wrapper, name, code, size, prologue_end, epilogue_begin,
					(WrapperType) wrapper_type, cil_code, lines);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				if (method != null)
					return method;

				method = new WrapperMethod (this);
				return method;
			}

			public override string ToString ()
			{
				return String.Format ("WrapperEntry [{0:x}:{3}:{1:x}:{2:x}]",
						      WrapperMethod, StartAddress, EndAddress, Name);
			}
		}

		protected class WrapperMethod : Method
		{
			public readonly WrapperEntry Entry;

			public WrapperMethod (WrapperEntry entry)
				: base (entry.Name, entry.File.ImageFile, entry.File.Module,
					entry.StartAddress, entry.EndAddress)
			{
				this.Entry = entry;
				SetMethodBounds (entry.MethodStartAddress, entry.MethodEndAddress);
				SetSource (new WrapperMethodSource (this));
				SetWrapperType (entry.WrapperType);
			}

			public override object MethodHandle {
				get { return Entry.WrapperMethod; }
			}

			public override TargetClassType DeclaringType {
				get { return null; }
			}

			public override bool HasThis {
				get { return false; }
			}

			public override TargetVariable This {
				get { throw new InvalidOperationException (); }
			}

			public override TargetVariable[] Parameters {
				get { return null; }
			}

			public override TargetVariable[] Locals {
				get { return null; }
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory,
								    TargetAddress address)
			{
				return Entry.File.LanguageBackend.GetTrampoline (memory, address);
			}
		}

		protected class WrapperMethodSource : MethodSource
		{
			WrapperMethod wrapper;

			public WrapperMethodSource (WrapperMethod wrapper)
				: base (wrapper, null)
			{
				this.wrapper = wrapper;
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

			protected override MethodSourceData ReadSource ()
			{
				ArrayList lines = new ArrayList ();
				int last_line = -1;

				JitLineNumberEntry[] line_numbers = wrapper.Entry.LineNumbers;

				string[] cil_code = wrapper.Entry.CILCode.Split ('\n');
				SourceBuffer buffer = new SourceBuffer (wrapper.Name, cil_code);

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

				return new MethodSourceData (
					1, cil_code.Length, addresses, null, buffer,
					wrapper.Entry.File.Module);
			}

		}

		protected struct TypeHashEntry
		{
			public readonly int Token;

			public TypeHashEntry (Cecil.ITypeDefinition type)
			{
				Token = (int) (type.MetadataToken.TokenType + type.MetadataToken.RID);
			}

			public TypeHashEntry (ClassEntry entry)
			{
				Token = entry.Token;
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

		protected class ClassEntry
		{
			public readonly MonoSymbolFile File;
			public readonly int Token;
			public readonly int InstanceSize;
			public readonly TargetAddress KlassAddress;
			readonly byte[] contents;

			public ClassEntry (MonoSymbolFile file, TargetReader reader, byte[] contents)
			{
				this.File = file;
				this.contents = contents;

				Token = reader.BinaryReader.ReadLeb128 ();
				InstanceSize = reader.BinaryReader.ReadLeb128 ();
				KlassAddress = reader.ReadAddress ();
			}

			public TargetBinaryReader Contents {
				get { return new TargetBinaryReader (contents, File.TargetInfo); }
			}

			public override string ToString ()
			{
				return String.Format ("ClassEntry [{0}:{1:x}:{2}:{3}]",
						      File, Token, InstanceSize, KlassAddress);
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
					ArrayList wrappers = file.WrapperEntries;

					ISymbolRange[] retval = new ISymbolRange [ranges.Count + wrappers.Count];
					ranges.CopyTo (retval, 0);
					wrappers.CopyTo (retval, ranges.Count);
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
