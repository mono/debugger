using System;
using System.IO;
using System.Text;
using R = System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Collections;
using System.Threading;
using C = Mono.CompilerServices.SymbolWriter;
using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Languages.Mono
{
	internal delegate void BreakpointHandler (Inferior inferior, TargetAddress address,
						  object user_data);

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

		// FIXME: Map mono/arch/x86/x86-codegen.h registers to
		//        debugger/arch/IArchitectureI386.cs registers.
		int[] register_map = { (int)I386Register.EAX, (int)I386Register.ECX,
				       (int)I386Register.EDX, (int)I386Register.EBX,
				       (int)I386Register.ESP, (int)I386Register.EBP,
				       (int)I386Register.ESI, (int)I386Register.EDI };

		public VariableInfo (TargetBinaryReader reader)
		{
			Index = reader.ReadLeb128 ();
			Offset = reader.ReadSLeb128 ();
			Size = reader.ReadLeb128 ();
			BeginLiveness = reader.ReadLeb128 ();
			EndLiveness = reader.ReadLeb128 ();

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

		public MethodAddress (C.MethodEntry entry, TargetBinaryReader reader, AddressDomain domain)
		{
			reader.Position = 16;
			StartAddress = ReadAddress (reader, domain);
			EndAddress = StartAddress + reader.ReadInt32 ();
			WrapperAddress = ReadAddress (reader, domain);

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

			HasThis = reader.ReadByte () != 0;
			if (HasThis)
				ThisVariableInfo = new VariableInfo (reader);

			int num_params = reader.ReadLeb128 ();
			ParamVariableInfo = new VariableInfo [num_params];
			for (int i = 0; i < num_params; i++)
				ParamVariableInfo [i] = new VariableInfo (reader);

			int num_locals = reader.ReadLeb128 ();
			LocalVariableInfo = new VariableInfo [num_locals];
			for (int i = 0; i < num_locals; i++)
				LocalVariableInfo [i] = new VariableInfo (reader);
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{5:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress, WrapperAddress);
		}
	}

	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress GenericTrampolineCode;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress GetVirtualMethod;
		public readonly TargetAddress GetBoxedObjectMethod;
		public readonly TargetAddress InsertBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupType;
		public readonly TargetAddress LookupAssembly;
		public readonly TargetAddress Heap;
		public readonly int HeapSize;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			reader.Offset = reader.TargetLongIntegerSize +
				2 * reader.TargetIntegerSize;
			GenericTrampolineCode = reader.ReadGlobalAddress ();
			SymbolTable = reader.ReadGlobalAddress ();
			SymbolTableSize = reader.ReadInteger ();
			CompileMethod = reader.ReadGlobalAddress ();
			GetVirtualMethod = reader.ReadGlobalAddress ();
			GetBoxedObjectMethod = reader.ReadGlobalAddress ();
			InsertBreakpoint = reader.ReadGlobalAddress ();
			RemoveBreakpoint = reader.ReadGlobalAddress ();
			RuntimeInvoke = reader.ReadGlobalAddress ();
			CreateString = reader.ReadGlobalAddress ();
			ClassGetStaticFieldData = reader.ReadGlobalAddress ();
			LookupType = reader.ReadGlobalAddress ();
			LookupAssembly = reader.ReadGlobalAddress ();
			Heap = reader.ReadAddress ();
			HeapSize = reader.ReadInteger ();
			Report.Debug (DebugFlags.JitSymtab, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x})",
				GenericTrampolineCode, SymbolTable, SymbolTableSize,
				CompileMethod, InsertBreakpoint, RemoveBreakpoint,
				RuntimeInvoke);
		}
	}

	internal class MonoBuiltinTypeInfo
	{
		public readonly MonoSymbolFile Corlib;
		public readonly MonoObjectType ObjectType;
		// public readonly MonoClass ValueType;
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
		// public readonly MonoClass EnumType;
		// public readonly MonoClass ArrayType;
		// public readonly MonoClass ExceptionType;

		public readonly int KlassFieldOffset;
		public readonly int KlassMethodsOffset;
		public readonly int KlassMethodCountOffset;
		public readonly int FieldInfoSize;

		public MonoBuiltinTypeInfo (MonoSymbolFile corlib, ITargetMemoryAccess memory,
					    TargetAddress address)
		{
			this.Corlib = corlib;

			int size = memory.ReadInteger (address);
			ITargetMemoryReader reader = memory.ReadMemory (address, size);
			reader.ReadInteger ();

			int defaults_size = reader.ReadInteger ();
			TargetAddress defaults_address = reader.ReadGlobalAddress ();

			KlassFieldOffset = reader.ReadInteger ();
			KlassMethodsOffset = reader.ReadInteger ();
			KlassMethodCountOffset = reader.ReadInteger ();
			FieldInfoSize = reader.ReadInteger ();

			ITargetMemoryReader mono_defaults = memory.ReadMemory (defaults_address, defaults_size);
			mono_defaults.ReadAddress ();

			TargetAddress klass = mono_defaults.ReadGlobalAddress ();
			int object_size = 2 * corlib.TargetInfo.TargetAddressSize;
			ObjectType = new MonoObjectType (corlib, typeof (object), object_size, klass);
			corlib.AddCoreType (ObjectType);

			klass = mono_defaults.ReadGlobalAddress ();
			ByteType = new MonoFundamentalType (corlib, typeof (byte), 1, klass);
			corlib.AddCoreType (ByteType);

			klass = mono_defaults.ReadGlobalAddress ();
			VoidType = new MonoOpaqueType (corlib, typeof (void));
			corlib.AddCoreType (VoidType);

			klass = mono_defaults.ReadGlobalAddress ();
			BooleanType = new MonoFundamentalType (corlib, typeof (bool), 1, klass);
			corlib.AddCoreType (BooleanType);

			klass = mono_defaults.ReadGlobalAddress ();
			SByteType = new MonoFundamentalType (corlib, typeof (sbyte), 1, klass);
			corlib.AddCoreType (SByteType);

			klass = mono_defaults.ReadGlobalAddress ();
			Int16Type = new MonoFundamentalType (corlib, typeof (short), 2, klass);
			corlib.AddCoreType (Int16Type);

			klass = mono_defaults.ReadGlobalAddress ();
			UInt16Type = new MonoFundamentalType (corlib, typeof (ushort), 2, klass);
			corlib.AddCoreType (UInt16Type);

			klass = mono_defaults.ReadGlobalAddress ();
			Int32Type = new MonoFundamentalType (corlib, typeof (int), 4, klass);
			Int32Type.Resolve ();
			corlib.AddCoreType (Int32Type);

			klass = mono_defaults.ReadGlobalAddress ();
			UInt32Type = new MonoFundamentalType (corlib, typeof (uint), 4, klass);
			corlib.AddCoreType (UInt32Type);

			klass = mono_defaults.ReadGlobalAddress ();
			IntType = new MonoFundamentalType (corlib, typeof (IntPtr), 4, klass);
			corlib.AddCoreType (IntType);

			klass = mono_defaults.ReadGlobalAddress ();
			UIntType = new MonoFundamentalType (corlib, typeof (UIntPtr), 4, klass);
			corlib.AddCoreType (UIntType);

			klass = mono_defaults.ReadGlobalAddress ();
			Int64Type = new MonoFundamentalType (corlib, typeof (long), 8, klass);
			corlib.AddCoreType (Int64Type);

			klass = mono_defaults.ReadGlobalAddress ();
			UInt64Type = new MonoFundamentalType (corlib, typeof (ulong), 8, klass);
			corlib.AddCoreType (UInt64Type);

			klass = mono_defaults.ReadGlobalAddress ();
			SingleType = new MonoFundamentalType (corlib, typeof (float), 4, klass);
			corlib.AddCoreType (SingleType);

			klass = mono_defaults.ReadGlobalAddress ();
			DoubleType = new MonoFundamentalType (corlib, typeof (double), 8, klass);
			corlib.AddCoreType (DoubleType);

			klass = mono_defaults.ReadGlobalAddress ();
			CharType = new MonoFundamentalType (corlib, typeof (char), 2, klass);
			corlib.AddCoreType (CharType);

			klass = mono_defaults.ReadGlobalAddress ();
			StringType = new MonoStringType (
				corlib, typeof (string), object_size, object_size + 4, klass);
			corlib.AddCoreType (StringType);
		}
	}

	internal class MonoSymbolFile : Module, ISymbolFile, ISimpleSymbolTable
	{
		internal readonly int Index;
		internal readonly R.Assembly Assembly;
		internal readonly R.Module Module;
		internal readonly TargetAddress MonoImage;
		internal readonly string ImageFile;
		internal readonly C.MonoSymbolFile File;
		internal readonly ThreadManager ThreadManager;
		internal readonly AddressDomain GlobalAddressDomain;
		internal readonly ITargetInfo TargetInfo;
		internal readonly MonoLanguageBackend MonoLanguage;
		protected readonly DebuggerBackend backend;
		readonly MonoSymbolTable symtab;
		readonly string name;
		readonly int address_size;
		readonly int int_size;

		Hashtable range_hash;
		ArrayList ranges;
		Hashtable type_hash;
		Hashtable class_entry_hash;
		ArrayList sources;
		Hashtable source_hash;
		Hashtable source_file_hash;
		Hashtable method_index_hash;

		internal MonoSymbolFile (MonoLanguageBackend language, DebuggerBackend backend,
					 ITargetInfo target_info, ITargetMemoryAccess memory,
					 TargetAddress address)
		{
			this.MonoLanguage = language;
			this.TargetInfo = target_info;
			this.backend = backend;

			ThreadManager = backend.ThreadManager;
			GlobalAddressDomain = memory.GlobalAddressDomain;

			address_size = TargetInfo.TargetAddressSize;
			int_size = TargetInfo.TargetIntegerSize;

			ranges = new ArrayList ();
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

			Assembly = R.Assembly.LoadFrom (ImageFile);
			Module = Assembly.GetModules () [0];

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			try {
				File = C.MonoSymbolFile.ReadSymbolFile (Assembly);
			} catch (Exception ex) {
				Console.WriteLine (ex.Message);
			}

			symtab = new MonoSymbolTable (this);

			name = Assembly.GetName (true).Name;

			backend.ModuleManager.AddModule (this);

			OnModuleChangedEvent ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
					      GetType (), ImageFile, IsLoaded,
					      SymbolsLoaded, StepInto, LoadSymbols);
		}

		internal void AddRangeEntry (ITargetMemoryReader reader, byte[] contents)
		{
			MethodRangeEntry range = MethodRangeEntry.Create (this, reader, contents);
			range_hash.Add (range.Index, range);
			ranges.Add (range);
		}

		internal void AddClassEntry (ITargetMemoryReader reader, byte[] contents)
		{
			ClassEntry entry = new ClassEntry (this, reader, contents);
			if (entry.Rank == 0)
				class_entry_hash.Add (new TypeHashEntry (entry), entry);
			else {
				Type etype = C.MonoDebuggerSupport.ResolveType (Module, entry.Token);
				Type atype = C.MonoDebuggerSupport.MakeArrayType (etype, entry.Rank);
				MonoType type = LookupType (atype);

				MonoLanguage.AddClass (entry.KlassAddress, type);
			}
		}

		public MonoType LookupType (Type type)
		{
			MonoType result = (MonoType) type_hash [type];
			if (result != null)
				return result;

			int rank = type.GetArrayRank ();
			if (rank > 0)
				result = new MonoArrayType (this, type);
			else
				result = new MonoClassType (this, type);

			type_hash.Add (type, result);
			return result;
		}

		internal void AddCoreType (MonoType type)
		{
			type.Resolve ();
			type_hash.Add (type.Type, type);
		}

		public void AddType (MonoType type)
		{
			type_hash.Add (type.Type, type);
		}

		public TargetBinaryReader GetTypeInfo (MonoType type)
		{
			ClassEntry entry = (ClassEntry) class_entry_hash [new TypeHashEntry (type.Type)];
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
				SourceFile info = new SourceFile (this, source.FileName);

				sources.Add (info);
				source_hash.Add (info, source);
				source_file_hash.Add (source, info);
			}
		}

		protected ArrayList SymbolRanges {
			get { return ranges; }
		}

		public override ISymbolTable SymbolTable {
			get { return symtab; }
		}

		public override string Name {
			get { return name; }
		}

		public override ILanguage Language {
			get { return MonoLanguage; }
		}

		internal override ILanguageBackend LanguageBackend {
			get { return MonoLanguage; }
		}

		public override ISymbolFile SymbolFile {
			get { return this; }
		}

		public override bool SymbolsLoaded {
			get { return LoadSymbols; }
		}

		public SourceFile[] Sources {
			get { return GetSources (); }
		}

		public override bool HasDebuggingInfo {
			get { return File != null; }
		}

		public override ISimpleSymbolTable SimpleSymbolTable {
			get { return this; }
		}

		public override TargetAddress SimpleLookup (string name)
		{
			return TargetAddress.Null;
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (MethodRangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address > range.EndAddress))
					continue;

				long offset = address - range.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				IMethod method = range.GetMethod ();
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

			R.MethodBase mbase = C.MonoDebuggerSupport.GetMethod (
				File.Assembly, entry.Token);

			StringBuilder sb = new StringBuilder (mbase.DeclaringType.FullName);
			sb.Append (".");
			sb.Append (mbase.Name);
			sb.Append ("(");
			bool first = true;
			foreach (R.ParameterInfo param in mbase.GetParameters ()) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (param.ParameterType.FullName);
			}
			sb.Append (")");

			string name = sb.ToString ();
			SourceMethod method = new SourceMethod (
				this, file, source.Index, name, source.StartRow,
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

		IMethod ISymbolFile.GetMethod (long handle)
		{
			MethodRangeEntry entry = (MethodRangeEntry) range_hash [(int) handle];
			Console.WriteLine ("GET METHOD BY HANDLE: {0} {1}", handle, entry);
			if (entry == null)
				return null;

			return entry.GetMethod ();
		}

		void ISymbolFile.GetMethods (SourceFile file)
		{
			ensure_sources ();
			C.SourceFileEntry source = (C.SourceFileEntry) source_hash [file];

			foreach (C.MethodSourceEntry entry in source.Methods)
				GetSourceMethod (file, entry.Index);
		}

		public override SourceMethod FindMethod (string name)
		{
			return null;
		}

		protected MonoMethod GetMonoMethod (int index)
		{
			ensure_sources ();
			MonoMethod mono_method = (MonoMethod) method_hash [index];
			if (mono_method != null)
				return mono_method;

			SourceMethod method = GetSourceMethod (index);
			C.MethodEntry entry = File.GetMethod (index);

			R.MethodBase mbase = C.MonoDebuggerSupport.GetMethod (
				File.Assembly, entry.Token);

			mono_method = new MonoMethod (this, method, entry, mbase);
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

		internal override IDisposable RegisterLoadHandler (Process process,
								   SourceMethod source,
								   MethodLoadedHandler handler,
								   object user_data)
		{
			MonoMethod method = GetMonoMethod ((int) source.Handle);
			return method.RegisterLoadHandler (process, handler, user_data);
		}

		internal override SimpleStackFrame UnwindStack (SimpleStackFrame frame,
								ITargetMemoryAccess memory)
		{
			return null;
		}

		protected class MonoMethod : MethodBase
		{
			MonoSymbolFile file;
			SourceMethod info;
			C.MethodEntry method;
			R.MethodBase rmethod;
			MonoClass decl_type;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable this_var;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			bool is_loaded;
			MethodAddress address;
			Hashtable load_handlers;

			public MonoMethod (MonoSymbolFile file, SourceMethod info,
					   C.MethodEntry method, R.MethodBase rmethod)
				: base (info.Name, file.ImageFile, file)
			{
				this.file = file;
				this.info = info;
				this.method = method;
				this.rmethod = rmethod;
			}

			public override object MethodHandle {
				get { return rmethod; }
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

				SetSource (new MonoMethodSource (file, this, info, method, address.LineNumbers));
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				R.ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				parameters = new IVariable [param_info.Length];
				for (int i = 0; i < param_info.Length; i++) {
					Type type = param_info [i].ParameterType;

					param_types [i] = file.MonoLanguage.LookupType (type);

					parameters [i] = new MonoVariable (
						file.backend, param_info [i].Name, param_types [i],
						false, param_types [i].IsByRef, this,
						address.ParamVariableInfo [i], 0, 0);
				}

				local_types = new MonoType [method.NumLocals];
				locals = new IVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					C.LocalVariableEntry local = method.Locals [i];
					Type type = C.MonoDebuggerSupport.GetLocalTypeFromSignature (
						file.Assembly, local.Signature);

					local_types [i] = file.MonoLanguage.LookupType (type);

#if FIXME
					if (method.LocalNamesAmbiguous && (local.BlockIndex > 0)) {
						int index = local.BlockIndex - 1;
						JitLexicalBlockEntry block = address.LexicalBlocks [index];
						locals [i] = new MonoVariable (
							file.backend, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [i],
							block.StartAddress, block.EndAddress);
					} else {
						locals [i] = new MonoVariable (
							file.backend, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [i]);
					}
#else
					locals [i] = new MonoVariable (
						file.backend, local.Name, local_types [i],
						true, local_types [i].IsByRef, this,
						address.LocalVariableInfo [i]);
#endif
				}

				decl_type = (MonoClass) file.MonoLanguage.LookupType (rmethod.DeclaringType);

				if (address.HasThis)
					this_var = new MonoVariable (
						file.backend, "this", decl_type, true,
						true, this, address.ThisVariableInfo);

				has_variables = true;
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

			public override ITargetStructType DeclaringType {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return null;
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

			public override IVariable This {
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
			string GetTypeSignature (Type t)
			{
				switch (Type.GetTypeCode (t)) {
				case TypeCode.Char:
					return "char";
				case TypeCode.Boolean:
					return "bool";
				case TypeCode.Byte:
					return "byte";
				case TypeCode.SByte:
					return "sbyte";
				case TypeCode.Int16:
					return "int16";
				case TypeCode.UInt16:
					return "uint16";
				case TypeCode.Int32:
					return "int";
				case TypeCode.UInt32:
					return "uint";
				case TypeCode.Int64:
					return "long";
				case TypeCode.UInt64:
					return "ulong";
				case TypeCode.Single:
					return "single";
				case TypeCode.Double:
					return "double";
				case TypeCode.String:
					return "string";
				case TypeCode.Object:
				default:
					return t.FullName;
				}
			}

			public IDisposable RegisterLoadHandler (Process process,
								MethodLoadedHandler handler,
								object user_data)
			{
				StringBuilder sb = new StringBuilder ();
				sb.Append (rmethod.ReflectedType.FullName);
				sb.Append (":");
				sb.Append (rmethod.Name);
				sb.Append ("(");
				R.ParameterInfo[] pi = rmethod.GetParameters ();
				for (int i = 0; i < pi.Length; i++) {
					if (i > 0)
						sb.Append (",");
					sb.Append (GetTypeSignature (pi [i].ParameterType).Replace ('+','/'));
				}
				sb.Append (")");
				string full_name = sb.ToString ();

				if (load_handlers == null) {
					/* only insert the load handler breakpoint once */
					file.MonoLanguage.InsertBreakpoint (
						process, full_name,
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

			protected sealed class HandlerData : IDisposable
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

		protected class MonoMethodSource : MethodSource
		{
			int start_row, end_row;
			MonoSymbolFile file;
			JitLineNumberEntry[] line_numbers;
			C.MethodEntry method;
			SourceMethod source_method;
			IMethod imethod;
			SourceFileFactory factory;
			Hashtable namespaces;

			public MonoMethodSource (MonoSymbolFile file, IMethod imethod,
						 SourceMethod source_method, C.MethodEntry method,
						 JitLineNumberEntry[] line_numbers)
				: base (imethod, source_method.SourceFile)
			{
				this.file = file;
				this.imethod = imethod;
				this.method = method;
				this.line_numbers = line_numbers;
				this.source_method = source_method;
				this.start_row = method.StartRow;
				this.end_row = method.EndRow;
				this.factory = file.MonoLanguage.DebuggerBackend.SourceFileFactory;
			}

			void generate_line_number (ArrayList lines, TargetAddress address, int offset,
						   ref int last_line)
			{
				for (int i = method.NumLineNumbers - 1; i >= 0; i--) {
					C.LineNumberEntry lne = method.LineNumbers [i];

					if (lne.Offset > offset)
						continue;

					if (lne.Row > last_line) {
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
				return new MethodSourceData (start_row, end_row, addresses, source_method, buffer);
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

		private class MethodRangeEntry : SymbolRangeEntry
		{
			public readonly MonoSymbolFile File;
			public readonly int Index;
			public readonly TargetAddress WrapperAddress;
			readonly byte[] contents;

			private MethodRangeEntry (MonoSymbolFile file, int index,
						  TargetAddress start_address, TargetAddress end_address,
						  TargetAddress wrapper_address, byte[] contents)
				: base (start_address, end_address)
			{
				this.File = file;
				this.Index = index;
				this.WrapperAddress = wrapper_address;
				this.contents = contents;
			}

			public static MethodRangeEntry Create (MonoSymbolFile file, ITargetMemoryReader reader,
							       byte[] contents)
			{
				int domain = reader.ReadInteger ();
				int index = reader.ReadInteger ();
				TargetAddress start = reader.ReadGlobalAddress ();
				TargetAddress end = start + reader.ReadInteger ();
				TargetAddress wrapper = reader.ReadGlobalAddress ();

				return new MethodRangeEntry (file, index, start, end, wrapper, contents);
			}

			internal IMethod GetMethod ()
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

		protected struct TypeHashEntry
		{
			public readonly int Token;
			public readonly int Rank;

			public TypeHashEntry (Type type)
			{
				while (type.GetArrayRank () > 0)
					type = type.GetElementType ();

				Token = C.MonoDebuggerSupport.GetTypeToken (type);
				Rank = type.GetArrayRank ();
			}

			public TypeHashEntry (ClassEntry entry)
			{
				Token = entry.Token;
				Rank = entry.Rank;
			}

			public TypeHashEntry (int token)
			{
				Token = token;
				Rank = 0;
			}

			public override bool Equals (object o)
			{
				TypeHashEntry entry = (TypeHashEntry) o;
				return (entry.Token == Token) && (entry.Rank == Rank);
			}

			public override int GetHashCode ()
			{
				return Token;
			}

			public override string ToString ()
			{
				return String.Format ("TypeHashEntry ({0:x}:{1})", Token, Rank);
			}
		}

		protected class ClassEntry
		{
			public readonly MonoSymbolFile File;
			public readonly int Token;
			public readonly int Rank;
			public readonly int InstanceSize;
			public readonly TargetAddress KlassAddress;
			readonly byte[] contents;

			public ClassEntry (MonoSymbolFile file, ITargetMemoryReader reader, byte[] contents)
			{
				this.File = file;
				this.contents = contents;

				Token = reader.BinaryReader.ReadLeb128 ();
				Rank = reader.BinaryReader.ReadLeb128 ();
				InstanceSize = reader.BinaryReader.ReadLeb128 ();
				KlassAddress = reader.ReadGlobalAddress ();
			}

			public TargetBinaryReader Contents {
				get { return new TargetBinaryReader (contents, File.TargetInfo); }
			}

			public override string ToString ()
			{
				return String.Format ("ClassEntry [{0}:{1:x}:{2}:{3}:{4}]",
						      File, Token, Rank, InstanceSize, KlassAddress);
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

	internal class MonoLanguageBackend : ILanguage, ILanguageBackend
	{
		public const int  MinDynamicVersion = 48;
		public const int  MaxDynamicVersion = 48;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		ArrayList symbol_files;
		Hashtable assembly_hash;
		Hashtable class_hash;
		MonoSymbolFile corlib;
		MonoBuiltinTypeInfo builtin_types;

		int last_num_data_tables;
		int last_data_table_offset;

		DebuggerBackend backend;
		MonoDebuggerInfo info;
		TargetAddress trampoline_address;
		bool initialized;
		DebuggerMutex mutex;
		Heap heap;

		public MonoLanguageBackend (DebuggerBackend backend)
		{
			this.backend = backend;
			mutex = new DebuggerMutex ("mono_mutex");
		}

		public string Name {
			get { return "Mono"; }
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get { return info; }
		}

		internal MonoBuiltinTypeInfo BuiltinTypes {
			get { return builtin_types; }
		}

		public Heap DataHeap {
			get { return heap; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		void read_mono_debugger_info (ITargetMemoryAccess memory, Bfd bfd)
		{
			TargetAddress symbol_info = bfd ["MONO_DEBUGGER__debugger_info"];
			if (symbol_info.IsNull)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			ITargetMemoryReader header = memory.ReadMemory (symbol_info, 16);
			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version <MinDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at least {1}.", version, MinDynamicVersion);
			if (version > MaxDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at most {1}.", version, MaxDynamicVersion);

			int size = (int) header.ReadInteger ();

			ITargetMemoryReader table = memory.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			trampoline_address = memory.ReadGlobalAddress (info.GenericTrampolineCode);
			heap = new Heap ((ITargetInfo) memory, info.Heap, info.HeapSize);

			symbol_files = new ArrayList ();
			assembly_hash = new Hashtable ();
			class_hash = new Hashtable ();
		}

		void do_update_symbol_table (ITargetMemoryAccess memory)
		{
			Report.Debug (DebugFlags.JitSymtab, "Starting to update symbol table");
			backend.ModuleManager.Lock ();
			try {
				do_update (memory);
			} catch (ThreadAbortException) {
				return;
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
				return;
			} finally {
				backend.ModuleManager.UnLock ();
			}
			Report.Debug (DebugFlags.JitSymtab, "Done updating symbol table");
		}

		// This method reads the MonoDebuggerSymbolTable structure
		// (struct definition is in mono-debug-debugger.h)
		void do_update (ITargetMemoryAccess memory)
		{
			TargetAddress symtab_address = memory.ReadAddress (info.SymbolTable);
			ITargetMemoryReader header = memory.ReadMemory (symtab_address, info.SymbolTableSize);

			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"Debugger symbol table has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version < MinDynamicVersion)
				throw new SymbolTableException (
					"Debugger symbol table has version {0}, but " +
					"expected at least {1}.", version,
					MinDynamicVersion);
			if (version > MaxDynamicVersion)
				throw new SymbolTableException (
					"Debugger symbol table has version {0}, but " +
					"expected at most {1}.", version,
					MaxDynamicVersion);

			int total_size = header.ReadInteger ();
			if (total_size != info.SymbolTableSize)
				throw new SymbolTableException (
					"Debugger symbol table has size {0}, but " +
					"expected {1}.", total_size, info.SymbolTableSize);

			TargetAddress corlib_address = header.ReadGlobalAddress ();
			TargetAddress metadata_info = header.ReadGlobalAddress ();

			int num_symbol_files = header.ReadInteger ();
			TargetAddress symfiles_address = header.ReadAddress ();

			symfiles_address += symbol_files.Count * memory.TargetAddressSize;

			for (int i = symbol_files.Count; i < num_symbol_files; i++) {
				TargetAddress address = memory.ReadGlobalAddress (symfiles_address);
				symfiles_address += memory.TargetAddressSize;

				try {
					MonoSymbolFile symfile = new MonoSymbolFile (
						this, backend, memory, memory, address);
					symbol_files.Add (symfile);
					assembly_hash.Add (symfile.Assembly, symfile);

					if (address != corlib_address)
						continue;

					corlib = symfile;
					builtin_types = new MonoBuiltinTypeInfo (corlib, memory, metadata_info);
				} catch (C.MonoSymbolFileException ex) {
					Console.WriteLine (ex.Message);
				} catch (Exception ex) {
					Console.WriteLine (ex.Message);
				}
			}

			read_data_table (memory, header);
		}

		// This method reads a portion of the data table (defn in mono-debug-debugger.h)
		void read_data_table (ITargetMemoryAccess memory, ITargetMemoryReader header)
		{
			int num_data_tables = header.ReadInteger ();
			int chunk_size = header.ReadInteger ();
			TargetAddress data_tables = header.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, "DATA TABLES: {0} {1} {2} {3}",
				      last_num_data_tables, num_data_tables, chunk_size,
				      data_tables);

			if (num_data_tables != last_num_data_tables) {
				int old_offset = 0;
				int old_count = num_data_tables;
				int old_size = old_count * chunk_size;

				for (int i = 0; i < num_data_tables; i++) {
					TargetAddress old_table = memory.ReadAddress (data_tables);
					data_tables += memory.TargetAddressSize;

					read_data_items (memory, old_table, old_offset, chunk_size);
					old_offset += chunk_size;
				}

				last_num_data_tables = num_data_tables;
				last_data_table_offset = old_size;
			}

			TargetAddress data_table_address = header.ReadAddress ();
			int data_table_total_size = header.ReadInteger ();
			int offset = header.ReadInteger ();
			int start = header.ReadInteger ();

			int size = offset - last_data_table_offset;
			int read_offset = last_data_table_offset - start;

			Report.Debug (DebugFlags.JitSymtab,
				      "DATA TABLE: {0} {1} {2} {3} - {4} {5}",
				      data_table_address, data_table_total_size,
				      offset, start, read_offset, size);

			if (size != 0) {
				data_table_address += read_offset;
				read_data_items (memory, data_table_address, last_data_table_offset, size);
			}

			last_data_table_offset = offset;
		}

		void read_data_items (ITargetMemoryAccess memory, TargetAddress address, int offset, int size)
		{
			ITargetMemoryReader reader = memory.ReadMemory (address, size);

			Report.Debug (DebugFlags.JitSymtab,
				      "READ DATA ITEMS: {0} {1} {2} {3} - {4} {5}", address, offset,
				      address + offset, size, reader.BinaryReader.Position, reader.Size);

			while (reader.BinaryReader.Position < reader.Size) {
				int item_size = reader.BinaryReader.ReadInt32 ();
				DataItemType item_type = (DataItemType) reader.BinaryReader.ReadInt32 ();

				long pos = reader.BinaryReader.Position;

				switch (item_type) {
				case DataItemType.Method:
					read_range_entry (reader);
					break;

				case DataItemType.Class:
					read_class_entry (reader);
					break;
				}

				reader.BinaryReader.Position = pos + item_size;
				offset += item_size + 8;
			}
		}

		void read_range_entry (ITargetMemoryReader reader)
		{
			int size = reader.BinaryReader.PeekInt32 ();
			byte[] contents = reader.BinaryReader.PeekBuffer (size);
			reader.BinaryReader.ReadInt32 ();
			MonoSymbolFile file = (MonoSymbolFile) symbol_files [reader.BinaryReader.ReadInt32 ()];
			file.AddRangeEntry (reader, contents);
		}

		void read_class_entry (ITargetMemoryReader reader)
		{
			int size = reader.BinaryReader.PeekInt32 ();
			byte[] contents = reader.BinaryReader.PeekBuffer (size);
			reader.BinaryReader.ReadInt32 ();
			MonoSymbolFile file = (MonoSymbolFile) symbol_files [reader.BinaryReader.ReadInt32 ()];
			file.AddClassEntry (reader, contents);
		}

		public MonoType LookupType (Type type)
		{
			MonoSymbolFile file = (MonoSymbolFile) assembly_hash [type.Assembly];
			if (file == null) {
				Console.WriteLine ("Type `{0}' from unknown assembly `{1}'", type, type.Assembly);
				return null;
			}

			return file.LookupType (type);
		}

		public void AddClass (TargetAddress klass_address, MonoType type)
		{
			class_hash.Add (klass_address, type);
		}

		public MonoType GetClass (TargetAddress klass_address)
		{
			return (MonoType) class_hash [klass_address];
		}

		private enum DataItemType {
			Unknown		= 0,
			Method,
			Class
		}

		private struct DataItem {
			public readonly int Offset;
			public readonly int Size;
			public readonly DataItemType Type;
			public readonly byte[] Data;

			public DataItem (int offset, int size, DataItemType type, byte[] data)
			{
				this.Offset = offset;
				this.Size = size;
				this.Type = type;
				this.Data = data;

				Console.WriteLine ("DATA ITEM: {0} {1} {2} {3}", offset, size, type,
						   TargetBinaryReader.HexDump (data));
			}
		}

		private ITargetType LookupType (StackFrame frame, Type type, string name)
		{
			return null;
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			switch (name) {
			case "short":
				name = "System.Int16";
				break;
			case "ushort":
				name = "System.UInt16";
				break;
			case "int":
				name = "System.Int32";
				break;
			case "uint":
				name = "System.UInt32";
				break;
			case "long":
				name = "System.Int64";
				break;
			case "ulong":
				name = "System.UInt64";
				break;
			case "float":
				name = "System.Single";
				break;
			case "double":
				name = "System.Double";
				break;
			case "char":
				name = "System.Char";
				break;
			case "byte":
				name = "System.Byte";
				break;
			case "sbyte":
				name = "System.SByte";
				break;
			case "object":
				name = "System.Object";
				break;
			case "string":
				name = "System.String";
				break;
			case "bool":
				name = "System.Boolean";
				break;
			case "void":
				name = "System.Void";
				break;
			case "decimal":
				name = "System.Decimal";
				break;
			}

			if (name.IndexOf ('[') >= 0)
				return null;

			foreach (MonoSymbolFile symfile in symbol_files) {
				Type type = symfile.Assembly.GetType (name);
				if (type == null)
					continue;

#if FIXME
				MonoType mtype = (MonoType) types [type];
				if (mtype != null)
					return mtype;

				return LookupType (frame, type, name);
#endif
			}

			return null;
		}

		public bool CanCreateInstance (Type type)
		{
			return false;
		}

		public ITargetObject CreateInstance (StackFrame frame, object obj)
		{
			return null;
		}

		public ITargetPointerObject CreatePointer (StackFrame frame, TargetAddress address)
		{
			return backend.BfdContainer.NativeLanguage.CreatePointer (frame, address);
		}

		public ITargetObject CreateObject (StackFrame frame, TargetAddress address)
		{
			return null;
		}

		ITargetFundamentalType ILanguage.IntegerType {
			get { return null; }
		}

		ITargetFundamentalType ILanguage.LongIntegerType {
			get { return null; }
		}

		ITargetFundamentalType ILanguage.StringType {
			get { return null; }
		}

		ITargetType ILanguage.PointerType {
			get { return null; }
		}

		ITargetType ILanguage.ExceptionType {
			get { return null; }
		}

		Hashtable breakpoints = new Hashtable ();

		internal int InsertBreakpoint (Process process, string method_name,
					       BreakpointHandler handler, object user_data)
		{
			long retval = process.CallMethod (info.InsertBreakpoint, 0, method_name);

			int index = (int) retval;

			if (index <= 0)
				return -1;

			breakpoints.Add (index, new MyBreakpointHandle (index, handler, user_data));
			return index;
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
			get { return trampoline_address; }
		}

		public TargetAddress RuntimeInvokeFunc {
			get { return info.RuntimeInvoke; }
		}

		TargetAddress ILanguageBackend.GetTrampolineAddress (ITargetMemoryAccess memory,
								     TargetAddress address,
								     out bool is_start)
		{
			is_start = false;

			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			return memory.Architecture.GetTrampoline (memory, address, trampoline_address);
		}

		TargetAddress ILanguageBackend.CompileMethodFunc {
			get { return info.CompileMethod; }
		}

		TargetAddress ILanguageBackend.GetVirtualMethodFunc {
			get { return info.GetVirtualMethod; }
		}

		public TargetAddress GetBoxedObjectFunc {
			get { return info.GetBoxedObjectMethod; }
		}

		SourceMethod ILanguageBackend.GetTrampoline (ITargetMemoryAccess memory,
							     TargetAddress address)
		{
			return null;
		}

		public void Notification (Inferior inferior, NotificationType type,
					  TargetAddress data, long arg)
		{
			switch (type) {
			case NotificationType.InitializeManagedCode:
				read_mono_debugger_info (inferior, inferior.Bfd);
				do_update_symbol_table (inferior);
				break;

			case NotificationType.ReloadSymtabs:
				do_update_symbol_table (inferior);
				break;

			case NotificationType.JitBreakpoint:
				if (!breakpoints.Contains ((int) arg))
					break;

				do_update_symbol_table (inferior);

				MyBreakpointHandle handle = (MyBreakpointHandle) breakpoints [(int) arg];
				handle.Handler (inferior, data, handle.UserData);
				breakpoints.Remove (arg);
				break;

			case NotificationType.MethodCompiled:
				do_update_symbol_table (inferior);
				break;

			default:
				Console.WriteLine ("Received unknown notification {0:x}",
						   (int) type);
				break;
			}
		}
	}
}
