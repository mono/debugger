using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
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

	// <summary>
	//   This class is the managed representation of the MONO_DEBUGGER__debugger_info struct.
	//   as defined in debugger/wrapper/mono-debugger-jit-wrapper.h
	// </summary>
	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress MonoTrampolineCode;
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
			/* skip past magic, version, and total_size */
			reader.Offset = 16;

			SymbolTableSize         = reader.ReadInteger ();
			HeapSize                = reader.ReadInteger ();
			MonoTrampolineCode      = reader.ReadGlobalAddress ();
			SymbolTable             = reader.ReadGlobalAddress ();
			CompileMethod           = reader.ReadGlobalAddress ();
			GetVirtualMethod        = reader.ReadGlobalAddress ();
			GetBoxedObjectMethod    = reader.ReadGlobalAddress ();
			InsertBreakpoint        = reader.ReadGlobalAddress ();
			RemoveBreakpoint        = reader.ReadGlobalAddress ();
			RuntimeInvoke           = reader.ReadGlobalAddress ();
			CreateString            = reader.ReadGlobalAddress ();
			ClassGetStaticFieldData = reader.ReadGlobalAddress ();
			LookupType              = reader.ReadGlobalAddress ();
			LookupAssembly          = reader.ReadGlobalAddress ();
			Heap                    = reader.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x})",
				MonoTrampolineCode, SymbolTable, SymbolTableSize,
				CompileMethod, InsertBreakpoint, RemoveBreakpoint,
				RuntimeInvoke);
		}
	}

	// <summary>
	//   This class is the managed representation of the
	//   MonoDefaults struct (at least the types we're interested
	//   in) as defined in mono/metadata/class-internals.h.
	// </summary>
	internal class MonoBuiltinTypeInfo
	{
		public readonly MonoSymbolFile Corlib;
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
		public readonly MonoClassType ExceptionType;
		public readonly MonoClassType DelegateType;

		public readonly int TypeSize;
		public readonly int ArrayTypeSize;
		public readonly int KlassSize;
		public readonly int KlassInstanceSizeOffset;
		public readonly int KlassTokenOffset;
		public readonly int KlassFieldOffset;
		public readonly int KlassMethodsOffset;
		public readonly int KlassMethodCountOffset;
		public readonly int KlassThisArgOffset;
		public readonly int KlassByValArgOffset;
		public readonly int KlassGenericClassOffset;
		public readonly int KlassGenericContainerOffset;
		public readonly int FieldInfoSize;

		public MonoBuiltinTypeInfo (MonoSymbolFile corlib, ITargetMemoryAccess memory,
					    TargetAddress address)
		{
			this.Corlib = corlib;

			int size = memory.ReadInteger (address);
			TargetBinaryReader reader = memory.ReadMemory (address, size).GetReader ();
			reader.ReadInt32 ();

			int defaults_size = reader.ReadInt32 ();
			TargetAddress defaults_address = new TargetAddress (
				memory.GlobalAddressDomain, reader.ReadAddress ());

			TypeSize = reader.ReadInt32 ();
			ArrayTypeSize = reader.ReadInt32 ();
			KlassSize = reader.ReadInt32 ();
			KlassInstanceSizeOffset = reader.ReadInt32 ();
			KlassTokenOffset = reader.ReadInt32 ();
			KlassFieldOffset = reader.ReadInt32 ();
			KlassMethodsOffset = reader.ReadInt32 ();
			KlassMethodCountOffset = reader.ReadInt32 ();
			KlassThisArgOffset = reader.ReadInt32 ();
			KlassByValArgOffset = reader.ReadInt32 ();
			KlassGenericClassOffset = reader.ReadInt32 ();
			KlassGenericContainerOffset = reader.ReadInt32 ();
			FieldInfoSize = reader.ReadInt32 ();

			TargetReader mono_defaults = new TargetReader (
				memory.ReadMemory (defaults_address, defaults_size).Contents, memory);
			mono_defaults.ReadAddress ();

			TargetAddress klass = mono_defaults.ReadGlobalAddress ();
			int object_size = 2 * corlib.TargetInfo.TargetAddressSize;
			Cecil.ITypeDefinition object_type = corlib.Module.Types ["System.Object"];
			ObjectType = new MonoObjectType (corlib, object_type, object_size, klass);
			corlib.AddCoreType (ObjectType, object_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition byte_type = corlib.Module.Types ["System.Byte"];
			ByteType = new MonoFundamentalType (corlib, byte_type, FundamentalKind.Byte, 1, klass);
			corlib.AddCoreType (ByteType, byte_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition void_type = corlib.Module.Types ["System.Void"];
			VoidType = new MonoOpaqueType (corlib, void_type);
			corlib.AddCoreType (VoidType, void_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition bool_type = corlib.Module.Types ["System.Boolean"];
			BooleanType = new MonoFundamentalType (corlib, bool_type, FundamentalKind.Byte, 1, klass);
			corlib.AddCoreType (BooleanType, bool_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition sbyte_type = corlib.Module.Types ["System.SByte"];
			SByteType = new MonoFundamentalType (corlib, sbyte_type, FundamentalKind.SByte, 1, klass);
			corlib.AddCoreType (SByteType, sbyte_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition short_type = corlib.Module.Types ["System.Int16"];
			Int16Type = new MonoFundamentalType (corlib, short_type, FundamentalKind.Int16, 2, klass);
			corlib.AddCoreType (Int16Type, short_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition ushort_type = corlib.Module.Types ["System.UInt16"];
			UInt16Type = new MonoFundamentalType (corlib, ushort_type, FundamentalKind.UInt16, 2, klass);
			corlib.AddCoreType (UInt16Type, ushort_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition int_type = corlib.Module.Types ["System.Int32"];
			Int32Type = new MonoFundamentalType (corlib, int_type, FundamentalKind.Int32, 4, klass);
			corlib.AddCoreType (Int32Type, int_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition uint_type = corlib.Module.Types ["System.UInt32"];
			UInt32Type = new MonoFundamentalType (corlib, uint_type, FundamentalKind.UInt32, 4, klass);
			corlib.AddCoreType (UInt32Type, uint_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition intptr_type = corlib.Module.Types ["System.IntPtr"];
			IntType = new MonoFundamentalType (corlib, intptr_type, FundamentalKind.IntPtr, 4, klass);
			corlib.AddCoreType (IntType, intptr_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition uintptr_type = corlib.Module.Types ["System.UIntPtr"];
			UIntType = new MonoFundamentalType (corlib, uintptr_type, FundamentalKind.Object, 4, klass);
			corlib.AddCoreType (UIntType, uintptr_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition long_type = corlib.Module.Types ["System.Int64"];
			Int64Type = new MonoFundamentalType (corlib, long_type, FundamentalKind.Int64, 8, klass);
			corlib.AddCoreType (Int64Type, long_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition ulong_type = corlib.Module.Types ["System.UInt64"];
			UInt64Type = new MonoFundamentalType (corlib, ulong_type, FundamentalKind.UInt64, 8, klass);
			corlib.AddCoreType (UInt64Type, ulong_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition float_type = corlib.Module.Types ["System.Single"];
			SingleType = new MonoFundamentalType (corlib, float_type, FundamentalKind.Single, 4, klass);
			corlib.AddCoreType (SingleType, float_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition double_type = corlib.Module.Types ["System.Double"];
			DoubleType = new MonoFundamentalType (corlib, double_type, FundamentalKind.Double, 8, klass);
			corlib.AddCoreType (DoubleType, double_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition char_type = corlib.Module.Types ["System.Char"];
			CharType = new MonoFundamentalType (corlib, char_type, FundamentalKind.Char, 2, klass);
			corlib.AddCoreType (CharType, char_type);

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition string_type = corlib.Module.Types ["System.String"];
			StringType = new MonoStringType (
				corlib, string_type, object_size, object_size + 4, klass);
			corlib.AddCoreType (StringType, string_type);

			// Skip a whole bunch of clases we don't care about
			mono_defaults.Offset += 2 * corlib.TargetInfo.TargetAddressSize;

			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition delegate_type = corlib.Module.Types ["System.Delegate"];
			DelegateType = new MonoClassType (corlib, delegate_type);
			corlib.AddCoreType (DelegateType, delegate_type);

			// Skip a whole bunch of clases we don't care about
			mono_defaults.Offset += 7 * corlib.TargetInfo.TargetAddressSize;

			// and get to the Exception class
			klass = mono_defaults.ReadGlobalAddress ();
			Cecil.ITypeDefinition exception_type = corlib.Module.Types ["System.Exception"];
			ExceptionType = new MonoClassType (corlib, exception_type);
			corlib.AddCoreType (ExceptionType, exception_type);
		}
	}

	internal class MonoLanguageBackend : MarshalByRefObject, ILanguage, ILanguageBackend
	{
		// These constants must match up with those in mono/mono/metadata/mono-debug.h
		public const int  MinDynamicVersion = 52;
		public const int  MaxDynamicVersion = 52;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		ArrayList symbol_files;
		int last_num_symbol_files;
		Hashtable image_hash;
		Hashtable assembly_hash;
		Hashtable assembly_by_name;
		Hashtable class_hash;
		MonoSymbolFile corlib;
		MonoBuiltinTypeInfo builtin_types;

		int last_num_data_tables;
		int last_data_table_offset;

		DebuggerBackend backend;
		MonoDebuggerInfo info;
		TargetAddress[] trampolines;
		bool initialized;
		DebuggerMutex mutex;
		Heap heap;

		public MonoLanguageBackend (DebuggerBackend backend)
		{
			this.backend = backend;
			mutex = new DebuggerMutex ("mono_mutex");
		}

		// needed for both ILanguage and ILanguageBackend interfaces
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

		public SourceFileFactory SourceFileFactory {
			get { return backend.SourceFileFactory; }
		}

		internal bool TryFindImage (Process process, string filename)
		{
			Cecil.IAssemblyDefinition ass = Cecil.AssemblyFactory.GetAssembly (filename);
			if (ass == null)
				return false;

			MonoSymbolFile file = (MonoSymbolFile) assembly_hash [ass];
			if (file != null)
				return true;

			return true;
		}

		public MonoType LookupMonoType (Cecil.ITypeReference type)
		{
			MonoSymbolFile file;

			Cecil.ITypeDefinition typedef = type as Cecil.ITypeDefinition;
			if (typedef != null) {
				file = (MonoSymbolFile) assembly_hash [type.Module.Assembly];
				if (file == null) {
					Console.WriteLine ("Type `{0}' from unknown assembly `{1}'",
							   type, type.Module.Assembly);
					return null;
				}

				return file.LookupMonoType (typedef);
			}

			Cecil.IArrayType array = type as Cecil.IArrayType;
			if (array != null) {
				MonoType element_type = LookupMonoType (array.ElementType);
				if (element_type == null)
					return null;

				return new MonoArrayType (element_type, array.Rank);
			}

			Cecil.IReferenceType reftype = type as Cecil.IReferenceType;
			if (reftype != null) {
				// FIXME FIXME FIXME
				return null;
			}

			if (type.Scope == null) {
				Console.WriteLine ("Cannot find type `{0}'", type);
				return null;
			}

			file = (MonoSymbolFile) assembly_by_name [type.Scope.Name];
			if (file == null) {
				Console.WriteLine ("Type `{0}' from unknown assembly `{1}'",
						   type, type.Scope.Name);
				return null;
			}

			int rank = 0;

			string full_name = type.FullName;
			int pos = full_name.IndexOf ('[');
			if (pos > 0) {
				string dim = full_name.Substring (pos);
				full_name = full_name.Substring (0, pos);

				if ((dim.Length < 2) || (dim [dim.Length - 1] != ']'))
					throw new ArgumentException ();
				for (int i = 1; i < dim.Length - 1; i++)
					if (dim [i] != ',')
						throw new ArgumentException ();

				rank = dim.Length - 1;
			}

			typedef = file.Module.Types [full_name];

			if (typedef == null) {
				Console.WriteLine ("Can't find type `{0}' in assembly `{1}'",
						   type.FullName, type.Scope.Name);
				return null;
			}

			MonoType mono_type = file.LookupMonoType (typedef);
			if (mono_type == null)
				return null;

			if (rank > 0)
				return new MonoArrayType (mono_type, rank);
			else
				return mono_type;
		}

		public void AddClass (TargetAddress klass_address, MonoType type)
		{
			if (!class_hash.Contains (klass_address))
				class_hash.Add (klass_address, type);
		}

		public MonoType GetClass (ITargetAccess target, TargetAddress klass_address)
		{
			MonoType type = (MonoType) class_hash [klass_address];
			if (type != null)
				return type;

			return MonoClassType.ReadMonoClass (this, target, klass_address);
		}

		public MonoSymbolFile GetImage (TargetAddress address)
		{
			return (MonoSymbolFile) image_hash [address];
		}

		void read_mono_debugger_info (ITargetMemoryAccess memory, Bfd bfd)
		{
			TargetAddress symbol_info = bfd ["MONO_DEBUGGER__debugger_info"];
			if (symbol_info.IsNull)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			TargetBinaryReader header = memory.ReadMemory (symbol_info, 16).GetReader ();
			long magic = header.ReadInt64 ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInt32 ();
			if (version < MinDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at least {1}.", version, MinDynamicVersion);
			if (version > MaxDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at most {1}.", version, MaxDynamicVersion);

			int size = header.ReadInt32 ();

			TargetReader table = new TargetReader (
				memory.ReadMemory (symbol_info, size), memory);
			info = new MonoDebuggerInfo (table);

			init_trampolines (memory);

			heap = new Heap ((ITargetInfo) memory, info.Heap, info.HeapSize);

			symbol_files = new ArrayList ();
			image_hash = new Hashtable ();
			assembly_hash = new Hashtable ();
			assembly_by_name = new Hashtable ();
			class_hash = new Hashtable ();
		}

		void init_trampolines (ITargetMemoryAccess memory)
		{
			trampolines = new TargetAddress [4];
			TargetAddress address = info.MonoTrampolineCode;
			trampolines [0] = memory.ReadGlobalAddress (address);
			address += memory.TargetAddressSize;
			trampolines [1] = memory.ReadGlobalAddress (address);
			address += memory.TargetAddressSize;
			trampolines [2] = memory.ReadGlobalAddress (address);
			address += 2 * memory.TargetAddressSize;
			trampolines [3] = memory.ReadGlobalAddress (address);
		}

#region symbol table management
		internal void Update (ITargetMemoryAccess target)
		{
			do_update_symbol_table (target);
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
			TargetReader header = new TargetReader (
				memory.ReadMemory (symtab_address, info.SymbolTableSize), memory);

			long magic = header.BinaryReader.ReadInt64 ();
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

			TargetAddress symfiles_address = header.ReadAddress ();
			int num_symbol_files = header.ReadInteger ();

			symfiles_address += last_num_symbol_files * memory.TargetAddressSize;
			for (int i = last_num_symbol_files; i < num_symbol_files; i++) {
				TargetAddress address = memory.ReadGlobalAddress (symfiles_address);
				symfiles_address += memory.TargetAddressSize;

				try {
					MonoSymbolFile symfile = new MonoSymbolFile (
						this, backend, memory, memory, address);
					image_hash.Add (symfile.MonoImage, symfile);
					symbol_files.Add (symfile);
					assembly_hash.Add (symfile.Assembly, symfile);
					assembly_by_name.Add (symfile.Assembly.Name.Name, symfile);

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

			last_num_symbol_files = num_symbol_files;
			read_data_table (memory, header);
		}

		// This method reads a portion of the data table (defn in mono-debug.h)
		void read_data_table (ITargetMemoryAccess memory, ITargetMemoryReader header)
		{
			int num_data_tables = header.ReadInteger ();
			TargetAddress data_tables = header.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, "DATA TABLES: {0} {1} {2}",
				      last_num_data_tables, num_data_tables, data_tables);

			if (num_data_tables != last_num_data_tables) {
				int old_offset = last_data_table_offset;

				data_tables += last_num_data_tables * memory.TargetAddressSize;

				for (int i = last_num_data_tables; i < num_data_tables; i++) {
					TargetAddress old_table = memory.ReadAddress (data_tables);
					data_tables += memory.TargetAddressSize;

					int old_size = memory.ReadInteger (old_table);
					read_data_items (memory, old_table, old_offset, old_size);
					old_offset = 0;
				}

				last_num_data_tables = num_data_tables;
				last_data_table_offset = 0;
			}

			TargetAddress data_table_address = header.ReadAddress ();
			int data_table_size = header.ReadInteger ();
			int offset = header.ReadInteger ();

			int size = offset - last_data_table_offset;

			Report.Debug (DebugFlags.JitSymtab,
				      "DATA TABLE: {0} {1} {2} - {3} {4}",
				      data_table_address, data_table_size, offset,
				      last_data_table_offset, size);

			if (size != 0)
				read_data_items (memory, data_table_address, last_data_table_offset, offset);

			last_data_table_offset = offset;
		}

		private enum DataItemType {
			Unknown		= 0,
			Method,
			Class,
			Wrapper
		}

		void read_data_items (ITargetMemoryAccess memory, TargetAddress address, int start, int end)
		{
			TargetReader reader = new TargetReader (
				memory.ReadMemory (address + start, end - start), memory);

			Report.Debug (DebugFlags.JitSymtab,
				      "READ DATA ITEMS: {0} {1} {2} - {3} {4}", address, start, end,
				      reader.BinaryReader.Position, reader.Size);

			if (start == 0)
				reader.BinaryReader.Position = memory.TargetAddressSize;

			while (reader.BinaryReader.Position + 4 < reader.Size) {
				int item_size = reader.BinaryReader.ReadInt32 ();
				if (item_size == 0)
					break;
				DataItemType item_type = (DataItemType) reader.BinaryReader.ReadInt32 ();

				long pos = reader.BinaryReader.Position;

				switch (item_type) {
				case DataItemType.Method:
					read_range_entry (reader);
					break;

				case DataItemType.Class:
					read_class_entry (reader);
					break;

				case DataItemType.Wrapper:
					read_wrapper_entry (memory, reader);
					break;
				}

				reader.BinaryReader.Position = pos + item_size;
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
			int file_idx = reader.BinaryReader.ReadInt32 ();

			if (file_idx >= symbol_files.Count)
				return;

			MonoSymbolFile file = (MonoSymbolFile) symbol_files [file_idx];
			if (file == null)
				return;

			file.AddClassEntry (reader, contents);
		}

		void read_wrapper_entry (ITargetMemoryAccess memory, ITargetMemoryReader reader)
		{
			int size = reader.BinaryReader.PeekInt32 ();
			byte[] contents = reader.BinaryReader.PeekBuffer (size);
			reader.BinaryReader.ReadInt32 ();
			corlib.AddWrapperEntry (memory, reader, contents);
		}
#endregion

#region jit breakpoint handling
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

		Hashtable breakpoints = new Hashtable ();

		internal int InsertBreakpoint (Process process, string method_name,
					       BreakpointHandler handler, object user_data)
		{
			TargetAddress retval = process.CallMethod (info.InsertBreakpoint, 0, method_name);

			int index = (int) retval.Address;

			if (index <= 0)
				return -1;

			breakpoints.Add (index, new MyBreakpointHandle (index, handler, user_data));
			return index;
		}
#endregion

#region ILanguage implementation
		public string SourceLanguage (StackFrame frame)
		{
			return "";
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			switch (name) {
			case "short":   name = "System.Int16";   break;
			case "ushort":  name = "System.UInt16";  break;
			case "int":     name = "System.Int32";   break;
			case "uint":    name = "System.UInt32";  break;
			case "long":    name = "System.Int64";   break;
			case "ulong":   name = "System.UInt64";  break;
			case "float":   name = "System.Single";  break;
			case "double":  name = "System.Double";  break;
			case "char":    name = "System.Char";    break;
			case "byte":    name = "System.Byte";    break;
			case "sbyte":   name = "System.SByte";   break;
			case "object":  name = "System.Object";  break;
			case "string":  name = "System.String";  break;
			case "bool":    name = "System.Boolean"; break;
			case "void":    name = "System.Void";    break;
			case "decimal": name = "System.Decimal"; break;
			}

			if (name.IndexOf ('[') >= 0)
				return null;

			// XXX this fixes #74391.  we basically need
			// to ensure the class has been initialized by
			// the runtime (and therefore had its
			// debugging info made available to the
			// debugger).
			mutex.Lock ();
			frame.Process.CallMethod (info.LookupType, 0, name);
			mutex.Unlock ();

			foreach (MonoSymbolFile symfile in symbol_files) {
				Cecil.ITypeDefinition type = symfile.Assembly.MainModule.Types [name];
				if (type == null)
					continue;
				return symfile.LookupMonoType (type);
			}

			return null;
		}

		MonoFundamentalType GetFundamentalType (Type type)
		{
			switch (Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
				return builtin_types.BooleanType;
			case TypeCode.Char:
				return builtin_types.CharType;
			case TypeCode.SByte:
				return builtin_types.SByteType;
			case TypeCode.Byte:
				return builtin_types.ByteType;
			case TypeCode.Int16:
				return builtin_types.Int16Type;
			case TypeCode.UInt16:
				return builtin_types.UInt16Type;
			case TypeCode.Int32:
				return builtin_types.Int32Type;
			case TypeCode.UInt32:
				return builtin_types.UInt32Type;
			case TypeCode.Int64:
				return builtin_types.Int64Type;
			case TypeCode.UInt64:
				return builtin_types.UInt64Type;
			case TypeCode.Single:
				return builtin_types.SingleType;
			case TypeCode.Double:
				return builtin_types.DoubleType;
			case TypeCode.String:
				return builtin_types.StringType;
			case TypeCode.Object:
				if (type == typeof (IntPtr))
					return builtin_types.IntType;
				else if (type == typeof (UIntPtr))
					return builtin_types.UIntType;
				return null;

			default:
				return null;
			}
		}

		public bool CanCreateInstance (Type type)
		{
			return GetFundamentalType (type) != null;
		}

		public ITargetObject CreateInstance (StackFrame frame, object obj)
		{
			MonoFundamentalType type = GetFundamentalType (obj.GetType ());
			if (type == null)
				return null;

			return type.CreateInstance (frame, obj);
		}

		public ITargetFundamentalObject CreateInstance (ITargetAccess target, int value)
		{
			return builtin_types.Int32Type.CreateInstance (target, value);
		}

		public ITargetPointerObject CreatePointer (StackFrame frame, TargetAddress address)
		{
			return backend.BfdContainer.NativeLanguage.CreatePointer (frame, address);
		}

		public ITargetObject CreateObject (ITargetAccess target, TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (target, address);
			MonoObjectObject obj = (MonoObjectObject)builtin_types.ObjectType.GetObject (location);
			if (obj == null)
				return null;

			ITargetObject result;
			try {
				result = obj.DereferencedObject;
				if (result == null)
					result = obj;
			} catch {
				result = obj;
			}

			return result;
		}

		public ITargetObject CreateNullObject (ITargetAccess target, ITargetType type)
		{
			TargetLocation location = new AbsoluteTargetLocation (target, TargetAddress.Null);

			return new MonoNullObject ((MonoType) type, location);
		}

		ITargetFundamentalType ILanguage.IntegerType {
			get { return builtin_types.Int32Type; }
		}

		ITargetFundamentalType ILanguage.LongIntegerType {
			get { return builtin_types.Int64Type; }
		}

		ITargetFundamentalType ILanguage.StringType {
			get { return builtin_types.StringType; }
		}

		ITargetType ILanguage.PointerType {
			get { return builtin_types.IntType; }
		}

		ITargetClassType ILanguage.ExceptionType {
			get { return builtin_types.ExceptionType; }
		}

		ITargetClassType ILanguage.DelegateType {
			get { return builtin_types.DelegateType; }
		}
#endregion

#region ILanguageBackend implementation
		public TargetAddress RuntimeInvokeFunc {
			get { return info.RuntimeInvoke; }
		}

		public TargetAddress GetTrampolineAddress (ITargetMemoryAccess memory,
							   TargetAddress address,
							   out bool is_start)
		{
			is_start = false;

			if (trampolines == null)
				return TargetAddress.Null;

			foreach (TargetAddress trampoline in trampolines) {
				TargetAddress result = memory.Architecture.GetTrampoline (
					memory, address, trampoline);
				if (!result.IsNull)
					return result;
			}

			return TargetAddress.Null;
		}

		public TargetAddress CompileMethodFunc {
			get { return info.CompileMethod; }
		}

		public TargetAddress GetVirtualMethodFunc {
			get { return info.GetVirtualMethod; }
		}

		public TargetAddress GetBoxedObjectFunc {
			get { return info.GetBoxedObjectMethod; }
		}

		public SourceMethod GetTrampoline (ITargetMemoryAccess memory,
						   TargetAddress address)
		{
			bool is_start;
			TargetAddress trampoline = GetTrampolineAddress (memory, address, out is_start);
			if (trampoline.IsNull)
				return null;

			int token = memory.ReadInteger (trampoline + 4);
			TargetAddress klass = memory.ReadGlobalAddress (trampoline + 8);
			TargetAddress image = memory.ReadGlobalAddress (klass);

			foreach (MonoSymbolFile file in symbol_files) {
				if (file.MonoImage != image)
					continue;

				return file.GetMethodByToken (token);
			}

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

			case NotificationType.AddModule:
				Report.Debug (DebugFlags.JitSymtab, "Module added");
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
#endregion

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			lock (this) {
				if (disposed)
					return;
			  
				disposed = true;
			}

			if (disposing) {
				if (symbol_files != null) {
					foreach (MonoSymbolFile symfile in symbol_files)
						symfile.Dispose();

					symbol_files = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~MonoLanguageBackend ()
		{
			Dispose (false);
		}

	}
}
