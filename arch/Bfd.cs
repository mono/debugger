using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Architecture
{
	internal delegate void BfdDisposedHandler (Bfd bfd);
 
	internal class Bfd : Module, ISymbolContainer, ILanguageBackend, IDisposable
	{
		IntPtr bfd;
		protected BfdContainer container;
		protected DebuggerBackend backend;
		protected ITargetMemoryAccess memory;
		protected Bfd core_file_bfd;
		protected Bfd main_bfd;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;
		int dynlink_breakpoint_id;
		Hashtable symbols;
		BfdSymbolTable simple_symtab;
		DwarfReader dwarf;
		bool dwarf_loaded;
		bool has_debugging_info;
		string filename;
		bool is_coredump;
		bool initialized;
		bool has_shlib_info;
		TargetAddress base_address, start_address, end_address;
		TargetAddress plt_start, plt_end, got_start;
		ILanguage language;
		bool has_got;

		[Flags]
		internal enum SectionFlags {
			Load = 1,
			Alloc = 2,
			ReadOnly = 4
		};

		internal class InternalSection
		{
			public readonly int index;
			public readonly int flags;
			public readonly long vma;
			public readonly long size;
			public readonly long section;

			public override string ToString ()
			{
				return String.Format ("Section [{0}:{1:x}:{2:x}:{3:x}:{4:x}]",
						      index, flags, vma, size, section);
			}
		}

		protected struct SymbolEntry : IComparable
		{
			public readonly long Address;
			public readonly string Name;

			public SymbolEntry (long address, string name)
			{
				this.Address = address;
				this.Name = name;
			}

			public int CompareTo (object obj)
			{
				SymbolEntry entry = (SymbolEntry) obj;

				if (entry.Address < Address)
					return 1;
				else if (entry.Address > Address)
					return -1;
				else
					return 0;
			}

			public override string ToString ()
			{
				return String.Format ("SymbolEntry ({0:x}:{1})",
						      Address, Name);
			}
		}

		internal class Section
		{
			public readonly Bfd bfd;
			public readonly long vma;
			public readonly long size;
			public readonly SectionFlags flags;
			public readonly ObjectCache contents;

			internal Section (Bfd bfd, InternalSection section)
			{
				this.bfd = bfd;
				this.vma = section.vma;
				this.size = section.size;
				this.flags = (SectionFlags) section.flags;
				contents = new ObjectCache (
					new ObjectCacheFunc (get_section_contents), section, 5);
			}

			object get_section_contents (object user_data)
			{
				InternalSection section = (InternalSection) user_data;

				byte[] data = bfd.GetSectionContents (new IntPtr (section.section), true);
				return new TargetReader (data, bfd.memory);
			}

			public ITargetMemoryReader GetReader (TargetAddress address)
			{
				ITargetMemoryReader reader = (ITargetMemoryReader) contents.Data;
				reader.Offset = address.Address - vma;
				return reader;
			}

			public override string ToString ()
			{
				return String.Format ("BfdSection ({0:x}:{1:x}:{2}:{3})", vma, size,
						      flags, bfd.FileName);
			}
		}

		[DllImport("libmonodebuggerbfdglue")]
		extern static void bfd_init ();

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr bfd_glue_openr (string filename, string target);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_close (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr bfd_get_section_by_name (IntPtr bfd, string name);

		[DllImport("libmonodebuggerbfdglue")]
		extern static string bfd_core_file_failing_command (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr bfd_glue_init_disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_check_format_object (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_check_format_core (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_get_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_get_dynamic_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_get_section_contents (IntPtr bfd, IntPtr section, bool raw_section, long offset, out IntPtr data, out int size);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_get_sections (IntPtr bfd, out IntPtr sections, out int count);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_get_section_by_name (IntPtr bfd, string name, out IntPtr section);

		[DllImport("libmonodebuggerbfdglue")]
		extern static long bfd_glue_elfi386_locate_base (IntPtr bfd, IntPtr data, int size);

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		[DllImport("libmonodebuggerbfdglue")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out int is_function, out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (BfdContainer container, ITargetMemoryAccess memory, string filename,
			    bool core_file, TargetAddress base_address)
		{
			this.container = container;
			this.memory = memory;
			this.filename = filename;
			this.base_address = base_address;
			this.backend = container.DebuggerBackend;

			language = new Mono.Debugger.Languages.Native.NativeLanguage (memory);

			bfd = bfd_glue_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new SymbolTableException ("Can't read symbol file: {0}", filename);

			if (core_file) {
				if (!bfd_glue_check_format_core (bfd))
					throw new SymbolTableException ("Not a core file: {0}", filename);

				is_coredump = true;

				return;
			}

			if (!bfd_glue_check_format_object (bfd))
				throw new SymbolTableException ("Not an object file: {0}", filename);

			InternalSection text = GetSectionByName (".text", true);
			InternalSection bss = GetSectionByName (".bss", true);

			if (!base_address.IsNull) {
				start_address = new TargetAddress (
					memory.GlobalAddressDomain, base_address.Address);
				end_address = start_address + bss.vma;
			} else {
				start_address = new TargetAddress (memory.GlobalAddressDomain, text.vma);
				end_address = new TargetAddress (memory.GlobalAddressDomain, bss.vma);
			}

			IntPtr symtab;
			int num_symbols = bfd_glue_get_symbols (bfd, out symtab);

			symbols = new Hashtable ();

			bool is_main_module = base_address.IsNull;

			for (int i = 0; i < num_symbols; i++) {
				string name;
				long address;
				int is_function;

				name = bfd_glue_get_symbol (bfd, symtab, i, out is_function, out address);

				if (name == null)
					continue;

				long relocated = base_address.Address + address;
				if (is_function != 0)
					symbols.Add (name, relocated);
				else if (is_main_module && name.StartsWith ("MONO_DEBUGGER__"))
					symbols.Add (name, relocated);
				else if (name.StartsWith ("__pthread_"))
					symbols.Add (name, relocated);
			}

			g_free (symtab);

			simple_symtab = new BfdSymbolTable (this);

			InternalSection plt_section = GetSectionByName (".plt", false);
			InternalSection got_section = GetSectionByName (".got", false);
			if ((plt_section != null) && (got_section != null)) {
				plt_start = new TargetAddress (
					memory.GlobalAddressDomain, base_address.Address + plt_section.vma);
				plt_end = plt_start + plt_section.size;

				got_start = new TargetAddress (
					memory.GlobalAddressDomain, base_address.Address + got_section.vma);
				has_got = true;
			}

			has_debugging_info = GetSectionByName (".debug_info", false) != null;

			backend.ModuleManager.AddModule (this);

			OnModuleChangedEvent ();
		}

		protected ArrayList GetSimpleSymbols ()
		{
			IntPtr symtab;
			ArrayList list = new ArrayList ();

			int num_symbols = bfd_glue_get_symbols (bfd, out symtab);

			for (int i = 0; i < num_symbols; i++) {
				string name;
				long address;
				int is_function;

				name = bfd_glue_get_symbol (bfd, symtab, i, out is_function, out address);
				if (name == null)
					continue;

				long relocated = base_address.Address + address;
				list.Add (new SymbolEntry (relocated, name));
			}

			g_free (symtab);

			num_symbols = bfd_glue_get_dynamic_symbols (bfd, out symtab);

			for (int i = 0; i < num_symbols; i++) {
				string name;
				long address;
				int is_function;

				name = bfd_glue_get_symbol (bfd, symtab, i, out is_function, out address);
				if (name == null)
					continue;

				long relocated = base_address.Address + address;
				list.Add (new SymbolEntry (relocated, name));
			}

			g_free (symtab);

			return list;
		}

		bool dynlink_handler (StackFrame frame, int index, object user_data)
		{
			if (memory.ReadInteger (rdebug_state_addr) != 0)
				return false;

			UpdateSharedLibraryInfo ((Inferior) user_data);
			return false;
		}

		bool read_dynamic_info (Inferior inferior)
		{
			if (initialized)
				return has_shlib_info;

			initialized = true;

			InternalSection section = GetSectionByName (".dynamic", false);
			if (section == null)
				return false;

			TargetAddress vma = new TargetAddress (inferior.AddressDomain, section.vma);

			int size = (int) section.size;
			byte[] dynamic = inferior.ReadBuffer (vma, size);

			TargetAddress debug_base;
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (dynamic, 0, data, size);
				long base_ptr = bfd_glue_elfi386_locate_base (bfd, data, size);
				if (base_ptr == 0)
					return false;
				debug_base = new TargetAddress (inferior.GlobalAddressDomain, base_ptr);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}

			ITargetMemoryReader reader = inferior.ReadMemory (debug_base, 20);
			if (reader.ReadInteger () != 1)
				return false;

			first_link_map = reader.ReadAddress ();
			dynlink_breakpoint = reader.ReadAddress ();

			rdebug_state_addr = debug_base + reader.Offset;

			if (reader.ReadInteger () != 0)
				return false;

			SimpleBreakpoint breakpoint = new SimpleBreakpoint (
				"dynlink", null,
				new BreakpointCheckHandler (dynlink_handler), null,
				false, inferior);

			dynlink_breakpoint_id = inferior.BreakpointManager.InsertBreakpoint (
				inferior, breakpoint, dynlink_breakpoint);

			has_shlib_info = true;
			return true;
		}

		public void UpdateSharedLibraryInfo (Inferior inferior)
		{
			// This fails if it's a statically linked executable.
			try {
				if (!read_dynamic_info (inferior))
					return;
			} catch {
				return;
			}

			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
				ITargetMemoryReader map_reader = inferior.ReadMemory (map, 16);

				TargetAddress l_addr = map_reader.ReadAddress ();
				TargetAddress l_name = map_reader.ReadAddress ();
				map_reader.ReadAddress ();

				string name;
				try {
					name = inferior.ReadString (l_name);
					// glibc 2.3.x uses the empty string for the virtual
					// "linux-gate.so.1".
					if ((name != null) && (name == ""))
						name = null;
				} catch {
					name = null;
				}

				map = map_reader.ReadAddress ();

				if (first) {
					first = false;
					continue;
				}

				if (name == null)
					continue;

				container.AddFile (inferior, name, StepInto, l_addr, null);
			}
		}

		public Bfd CoreFileBfd {
			get {
				return core_file_bfd;
			}

			set {
				core_file_bfd = value;
				if (core_file_bfd != null) {
#if FALSE
					InternalSection text = GetSectionByName (".text", true);

					base_address = new TargetAddress (
						memory.GlobalAddressDomain, text.vma);
					end_address = new TargetAddress (
						memory.GlobalAddressDomain, text.vma + text.size);
#endif
				}
			}
		}

		public Bfd MainBfd {
			get {
				return main_bfd;
			}

			set {
				main_bfd = value;
			}
		}

		public TargetAddress BaseAddress {
			get {
				return base_address;
			}
		}

		public TargetAddress GetAddress (long address)
		{
			if (BaseAddress.IsNull)
				return new TargetAddress (
					memory.GlobalAddressDomain, address);
			else
				return new TargetAddress (
					memory.GlobalAddressDomain, BaseAddress.Address + address);
		}

		public override string Name {
			get {
				return filename;
			}
		}

		public string FileName {
			get {
				return filename;
			}
		}

		public bool IsCoreDump {
			get {
				return is_coredump;
			}
		}

		public string CrashProgram {
			get {
				if (!is_coredump)
					throw new InvalidOperationException ();

				return bfd_core_file_failing_command (bfd);
			}
		}

		public override ISimpleSymbolTable SimpleSymbolTable {
			get {
				return simple_symtab;
			}
		}

		public override ILanguage Language {
			get {
				return language;
			}
		}

		internal override ILanguageBackend LanguageBackend {
			get {
				return this;
			}
		}

		public override bool SymbolsLoaded {
			get { return dwarf != null; }
		}

		public override SourceFile[] Sources {
			get {
				if (dwarf == null)
					return new SourceFile [0];

				return dwarf.Sources;
			}
		}

		public override ISymbolTable SymbolTable {
			get {
				if (dwarf == null)
					throw new InvalidOperationException ();

				return dwarf.SymbolTable;
			}
		}

		public override TargetAddress SimpleLookup (string name)
		{
			return this [name];
		}

		void load_dwarf ()
		{
			if (dwarf_loaded)
				return;

			try {
				dwarf = new DwarfReader (this, this, backend.SourceFileFactory);
			} catch {
				// Silently ignore.
			}

			dwarf_loaded = true;

			if (dwarf != null) {
				has_debugging_info = true;
				OnSymbolsLoadedEvent ();
			}
		}

		void unload_dwarf ()
		{
			if (!dwarf_loaded)
				return;

			dwarf_loaded = false;
			if (dwarf != null) {
				dwarf = null;
				OnSymbolsUnLoadedEvent ();
			}
		}

		protected override void OnModuleChangedEvent ()
		{
			if (LoadSymbols)
				load_dwarf ();
			else
				unload_dwarf ();

			base.OnModuleChangedEvent ();
		}

		public BfdDisassembler GetDisassembler (ITargetMemoryAccess memory)
		{
			IntPtr dis = disassembler (bfd);

			IntPtr info = bfd_glue_init_disassembler (bfd);

			return new BfdDisassembler (memory, dis, info);
		}

		public TargetAddress this [string name] {
			get {
				if (symbols == null)
					return TargetAddress.Null;

				if (symbols.Contains (name))
					return new TargetAddress (memory.GlobalAddressDomain, (long) symbols [name]);

				return TargetAddress.Null;
			}
		}

		internal Section this [long address] {
			get {
				read_sections ();
				foreach (Section section in sections) {
					if ((address < section.vma) || (address >= section.vma + section.size))
						continue;

					return section;
				}

				if (main_bfd != null)
					return main_bfd [address];

				throw new SymbolTableException (String.Format (
					"No section in file {1} contains address {0:x}.", address,
					filename));
			}
		}

		internal Section[] Sections {
			get {
				read_sections ();
				return sections;
			}
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			if (!is_coredump)
				throw new InvalidOperationException ();

			ArrayList list = new ArrayList ();

			read_sections ();

			ArrayList all_sections = new ArrayList ();
			all_sections.AddRange (sections);
			all_sections.AddRange (main_bfd.Sections);

			foreach (Section section in all_sections) {
				if ((section.flags & SectionFlags.Alloc) == 0)
					continue;

				if (section.size == 0)
					continue;

				TargetAddress start = new TargetAddress (
					memory.GlobalAddressDomain, section.vma);
				TargetAddress end = start + section.size;

				TargetMemoryFlags flags = 0;
				if ((section.flags & SectionFlags.ReadOnly) != 0)
					flags |= TargetMemoryFlags.ReadOnly;

				if (list.Count > 0) {
					TargetMemoryArea last = (TargetMemoryArea) list [list.Count - 1];

					if ((last.Flags == flags) &&
					    ((last.End + 1 == start) || (last.End == start))) {
						list [list.Count - 1] = new TargetMemoryArea (
							last.Start, end, last.Flags, last.Name);
						continue;
					}
				}

				string name = section.bfd.FileName;
				list.Add (new TargetMemoryArea (start, end, flags, name));
			}

			TargetMemoryArea[] maps = new TargetMemoryArea [list.Count];
			list.CopyTo (maps, 0);
			return maps;
		}

		public ITargetMemoryReader GetReader (TargetAddress address)
		{
			Section section = this [address.Address];
			return section.GetReader (address);
		}

		public byte[] GetSectionContents (string name, bool raw_section)
		{
			IntPtr section;

			section = bfd_get_section_by_name (bfd, name);
			if (section == IntPtr.Zero)
				return null;

			return GetSectionContents (section, raw_section);
		}

		byte[] GetSectionContents (IntPtr section, bool raw_section)
		{
			IntPtr data;
			int size;

			if (!bfd_glue_get_section_contents (bfd, section, raw_section, 0, out data, out size))
				return null;

			try {
				byte[] retval = new byte [size];
				Marshal.Copy (data, retval, 0, size);
				return retval;
			} finally {
				g_free (data);
			}
		}

		InternalSection GetSectionByName (string name, bool throw_exc)
		{
			IntPtr data = IntPtr.Zero;
			try {
				if (!bfd_glue_get_section_by_name (bfd, name, out data)) {
					if (throw_exc)
						throw new SymbolTableException (
							"Can't get bfd section {0}", name);
					else
						return null;
				}

				return (InternalSection) Marshal.PtrToStructure (
					data, typeof (InternalSection));
			} finally {
				g_free (data);
			}
		}

		bool has_sections = false;
		Section[] sections = null;

		protected void read_sections ()
		{
			if (has_sections)
				return;

			IntPtr data = IntPtr.Zero;
			try {
				int count;
				if (!bfd_glue_get_sections (bfd, out data, out count))
					throw new SymbolTableException ("Can't get bfd sections");

				sections = new Section [count];

				IntPtr ptr = data;
				for (int i = 0; i < count; i++) {
					InternalSection isection = (InternalSection) Marshal.PtrToStructure (
						ptr, typeof (InternalSection));
					sections [i] = new Section (this, isection);
					ptr = new IntPtr ((long) ptr + Marshal.SizeOf (isection));
				}
				has_sections = true;
			} finally {
				g_free (data);
			}
		}

		public Module Module {
			get {
				return this;
			}
		}

		public override bool HasDebuggingInfo {
			get {
				return has_debugging_info;
			}
		}

		//
		// ISymbolContainer
		//

		public bool IsContinuous {
			get {
				return !end_address.IsNull;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return start_address;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return end_address;
			}
		}

		//
		// ILanguageBackend
		//

		string ILanguageBackend.Name {
			get {
				return "Native";
			}
		}

		TargetAddress ILanguageBackend.GenericTrampolineCode {
			get {
				return TargetAddress.Null;
			}
		}

		TargetAddress ILanguageBackend.RuntimeInvokeFunc {
			get {
				return TargetAddress.Null;
			}
		}

		TargetAddress ILanguageBackend.CompileMethodFunc {
			get {
				return TargetAddress.Null;
			}
		}

		TargetAddress ILanguageBackend.GetTrampoline (Inferior inferior, TargetAddress address)
		{
			return GetTrampoline (address);
		}

		SourceMethod ILanguageBackend.GetTrampoline (TargetAddress address)
		{
			return null;
		}

		public TargetAddress GetTrampoline (TargetAddress address)
		{
			if (!has_got || (address < plt_start) || (address > plt_end))
				return TargetAddress.Null;

			ITargetMemoryReader reader = memory.ReadMemory (address, 10);

			byte opcode = reader.ReadByte ();
			byte opcode2 = reader.ReadByte ();
			if ((opcode != 0xff) || (opcode2 != 0x25))
				return TargetAddress.Null;

			TargetAddress jmp_target = reader.ReadAddress ();
			TargetAddress method = memory.ReadGlobalAddress (jmp_target);

			if (method != address + 6)
				return method;

			// FIXME: This is not yet implemented; we need to use LD_BIND_NOW=yes for
			//        the moment.

			return TargetAddress.Null;
		}

		//
		// The BFD symbol table.
		//

		private class BfdSymbolTable : ISimpleSymbolTable
		{
			Bfd bfd;
			SymbolEntry[] list;
			TargetAddress start, end;

			public BfdSymbolTable (Bfd bfd)
			{
				this.bfd = bfd;
				this.start = bfd.StartAddress;
				this.end = bfd.EndAddress;
			}

			public string SimpleLookup (TargetAddress address, bool exact_match)
			{
				if ((address < start) || (address >= end))
					return null;

				if (list == null) {
					ArrayList the_list = bfd.GetSimpleSymbols ();
					the_list.Sort ();

					list = new SymbolEntry [the_list.Count];
					the_list.CopyTo (list);
				}

				for (int i = list.Length - 1; i >= 0; i--) {
					SymbolEntry entry = list [i];

					if (address.Address < entry.Address)
						continue;

					long offset = address.Address - entry.Address;
					if (offset == 0) {
						while (i > 0) {
							SymbolEntry n_entry = list [--i];

							if (n_entry.Address == entry.Address) 
								entry = n_entry;
							else
								break;
						}

						return entry.Name;
					} else if (exact_match)
						return null;
					else
						return String.Format ("{0}+0x{1:x}", entry.Name, offset);
				}

				if (exact_match)
					return null;
				else
					return String.Format ("<{0}:0x{1:x}>", bfd.FileName, address-start);
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					bfd_close (bfd);
					bfd = IntPtr.Zero;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Bfd ()
		{
			Dispose (false);
		}
	}
}
