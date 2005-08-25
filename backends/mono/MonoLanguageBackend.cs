using System;
using System.IO;
using System.Text;
using R = System.Reflection;
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
			Int32Type.GetTypeInfo ();
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

			// Skip a whole bunch of clases we don't care about
			mono_defaults.Offset += 10 * corlib.TargetInfo.TargetAddressSize;

			// and get to the Exception class
			klass = mono_defaults.ReadGlobalAddress ();
			ExceptionType = new MonoClassType (corlib, typeof (Exception));
			corlib.AddCoreType (ExceptionType);
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
		Hashtable assembly_hash;
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
			R.Assembly ass = R.Assembly.LoadFrom (filename);
			if (ass == null)
				return false;

			MonoSymbolFile file = (MonoSymbolFile) assembly_hash [ass];
			if (file != null)
				return true;

			mutex.Lock ();
			process.CallMethod (info.LookupAssembly, 0, ass.Location);
			mutex.Unlock ();

			// always return true?
			return true;
		}

		public MonoType LookupMonoType (Type type)
		{
			MonoSymbolFile file = (MonoSymbolFile) assembly_hash [type.Assembly];
			if (file == null) {
				Console.WriteLine ("Type `{0}' from unknown assembly `{1}'", type, type.Assembly);
				return null;
			}

			return file.LookupMonoType (type);
		}

		public void AddClass (TargetAddress klass_address, MonoType type)
		{
			class_hash.Add (klass_address, type);
		}

		public MonoType GetClass (TargetAddress klass_address)
		{
			return (MonoType) class_hash [klass_address];
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
			if (version < MinDynamicVersion)
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

			init_trampolines (memory);

			heap = new Heap ((ITargetInfo) memory, info.Heap, info.HeapSize);

			symbol_files = new ArrayList ();
			assembly_hash = new Hashtable ();
			class_hash = new Hashtable ();
		}

		void init_trampolines (ITargetMemoryAccess memory)
		{
			trampolines = new TargetAddress [3];
			TargetAddress address = info.MonoTrampolineCode;
			trampolines [0] = memory.ReadGlobalAddress (address);
			address += memory.TargetAddressSize;
			trampolines [1] = memory.ReadGlobalAddress (address);
			address += memory.TargetAddressSize;
			trampolines [2] = memory.ReadGlobalAddress (address);
		}

#region symbol table management
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

			TargetAddress symfiles_address = header.ReadAddress ();
			int num_symbol_files = header.ReadInteger ();

			symfiles_address += last_num_symbol_files * memory.TargetAddressSize;
			for (int i = last_num_symbol_files; i < num_symbol_files; i++) {
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
			ITargetMemoryReader reader = memory.ReadMemory (address + start, end - start);

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

#if FIXME
			TargetAddress wrapper = reader.ReadGlobalAddress ();
			TargetAddress code = reader.ReadGlobalAddress ();
			int size = reader.ReadInteger ();

			TargetAddress naddr = memory.ReadAddress (
				wrapper + 8 + 2 * reader.TargetAddressSize);
			string name = "<" + memory.ReadString (naddr) + ">";

			corlib.AddWrapperEntry (wrapper, name, code, size);
#endif
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
				Type type = symfile.Assembly.GetType (name);
				if (type == null)
					continue;
				return symfile.LookupMonoType (type);
			}

			return null;
		}

		public bool CanCreateInstance (Type type)
		{
			return LookupMonoType (type) != null;
		}

		public ITargetObject CreateInstance (StackFrame frame, object obj)
		{
			MonoFundamentalType type = LookupMonoType (obj.GetType ()) as MonoFundamentalType;
			if (type == null)
				return null;

			return type.CreateInstance (frame, obj);
		}

		public ITargetPointerObject CreatePointer (StackFrame frame, TargetAddress address)
		{
			return backend.BfdContainer.NativeLanguage.CreatePointer (frame, address);
		}

		public ITargetObject CreateObject (ITargetAccess target, TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (null, target, address);
			MonoObjectObject obj = (MonoObjectObject)builtin_types.ObjectType.GetTypeInfo().GetObject (location);
			if (obj == null)
				return null;

			if (obj.HasDereferencedObject)
				return obj.DereferencedObject;
			else
				return obj;
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

		ITargetType ILanguage.ExceptionType {
			get { return builtin_types.ExceptionType; }
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
