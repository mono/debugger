using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal delegate void BfdDisposedHandler (Bfd bfd);
 
	internal class Bfd : SymbolFile, ISymbolContainer, ILanguageBackend, IDisposable
	{
		IntPtr bfd;
		protected Module module;
		protected BfdContainer container;
		protected ITargetMemoryInfo info;
		protected Bfd main_bfd;
		protected Architecture arch;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;
		TargetAddress entry_point = TargetAddress.Null;
		bool is_loaded;
		Hashtable load_handlers;
		Hashtable symbols;
		ArrayList simple_symbols;
		BfdSymbolTable simple_symtab;
		DwarfReader dwarf;
		DwarfFrameReader frame_reader, eh_frame_reader;
		bool dwarf_loaded;
		bool has_debugging_info;
		string filename, target;
		bool is_coredump;
		bool initialized;
		bool has_shlib_info;
		TargetAddress base_address, start_address, end_address;
		TargetAddress plt_start, plt_end, got_start;
		bool is_powerpc;
		bool has_got;

		[Flags]
		internal enum SectionFlags {
			Load = 1,
			Alloc = 2,
			ReadOnly = 4
		};

		[StructLayout(LayoutKind.Sequential)]
		internal class InternalSection
		{
			public readonly int index;
			public readonly int flags;
			public readonly long vma;
			public readonly long size;
			public readonly long section;
			public readonly string name;

			public override string ToString ()
			{
				return String.Format ("Section [{0}:{1:x}:{2:x}:{3:x}:{4:x}]",
						      index, flags, vma, size, section);
			}
		}

		internal class Section
		{
			public readonly Bfd bfd;
			public readonly string name;
			public readonly long vma;
			public readonly long size;
			public readonly SectionFlags flags;
			public readonly ObjectCache contents;

			internal Section (Bfd bfd, InternalSection section)
			{
				this.bfd = bfd;
				this.name = section.name;
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
				if (data == null)
					throw new SymbolTableException ("Can't get bfd section {0}", name);
				return new TargetReader (data, bfd.info);
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

		[DllImport("monodebuggerserver")]
		extern static void bfd_init ();

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_openr (string filename, string target);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_close (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_get_target_name (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_get_section_by_name (IntPtr bfd, string name);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_core_file_failing_command (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static IntPtr disassembler (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_init_disassembler (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_check_format_object (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_check_format_core (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_get_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_get_dynamic_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_get_section_contents (IntPtr bfd, IntPtr section, bool raw_section, long offset, out IntPtr data, out int size);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_get_sections (IntPtr bfd, out IntPtr sections, out int count);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_get_section_by_name (IntPtr bfd, string name, out IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static long bfd_glue_elfi386_locate_base (IntPtr bfd, IntPtr data, int size);

		[DllImport("libglib-2.0-0.dll")]
		extern static void g_free (IntPtr data);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out int is_function, out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (BfdContainer container, ITargetMemoryInfo info, string filename,
			    Bfd main_bfd, TargetAddress base_address, bool is_loaded)
		{
			this.container = container;
			this.info = info;
			this.filename = filename;
			this.base_address = base_address;
			this.main_bfd = main_bfd;
			this.is_loaded = is_loaded;

			load_handlers = new Hashtable ();

			bfd = bfd_glue_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new SymbolTableException ("Can't read symbol file: {0}", filename);

			if (bfd_glue_check_format_object (bfd))
				is_coredump = false;
			else if (bfd_glue_check_format_core (bfd))
				is_coredump = true;
			else
				throw new SymbolTableException ("Not an object file: {0}", filename);

			target = bfd_glue_get_target_name (bfd);
			if ((target == "elf32-i386") || (target == "elf64-x86-64")) {
				if (target == "elf32-i386")
					arch = new Architecture_I386 (container.Process);
				else
					arch = new Architecture_X86_64 (container.Process);

				if (!is_coredump) {
					InternalSection text = GetSectionByName (".text", true);

					if (!base_address.IsNull)
						start_address = new TargetAddress (
							info.AddressDomain,
							base_address.Address + text.vma);
					else
						start_address = new TargetAddress (
							info.AddressDomain, text.vma);
					end_address = start_address + text.size;
				} else {
					start_address = main_bfd.StartAddress;
					end_address = main_bfd.EndAddress;
				}

				read_bfd_symbols ();

				if (DwarfReader.IsSupported (this))
					has_debugging_info = true;

				InternalSection plt_section = GetSectionByName (".plt", false);
				InternalSection got_section = GetSectionByName (".got", false);
				if ((plt_section != null) && (got_section != null)) {
					plt_start = new TargetAddress (
						info.AddressDomain,
						base_address.Address + plt_section.vma);
					plt_end = plt_start + plt_section.size;

					got_start = new TargetAddress (
						info.AddressDomain,
						base_address.Address + got_section.vma);
					has_got = true;
				}
			} else
				throw new SymbolTableException (
					"Symbol file {0} has unknown target architecture {1}",
					filename, target);

			entry_point = this ["main"];

			module = container.Process.ModuleManager.GetModule (filename);
			if (module == null) {
				module = new Module (filename, this);
				container.Process.ModuleManager.AddModule (module);
				OnModuleChanged ();
			} else {
				module.LoadModule (this);
			}
		}

		public Bfd OpenCoreFile (string core_file)
		{
			Bfd core = new Bfd (container, info, core_file, this, TargetAddress.Null, true);
			core.is_coredump = true;
			return core;
		}

		void read_bfd_symbols ()
		{
			IntPtr symtab;
			int num_symbols = bfd_glue_get_symbols (bfd, out symtab);

			symbols = new Hashtable ();
			simple_symbols = new ArrayList ();

			for (int i = 0; i < num_symbols; i++) {
				string name;
				long address;
				int is_function;

				name = bfd_glue_get_symbol (bfd, symtab, i, out is_function, out address);
				if (name == null)
					continue;

				TargetAddress relocated = new TargetAddress (
					info.AddressDomain, base_address.Address + address);
				if (is_function != 0)
					symbols.Add (name, relocated);
				else if ((main_bfd == null) && name.StartsWith ("MONO_DEBUGGER__"))
					symbols.Add (name, relocated);
				else if (name.StartsWith ("__pthread_") ||
					 (name == "__libc_pthread_functions"))
					symbols.Add (name, relocated);

				simple_symbols.Add (new Symbol (name, relocated, 0));
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

				TargetAddress relocated = new TargetAddress (
					info.AddressDomain,
					base_address.Address + address);
				simple_symbols.Add (new Symbol (name, relocated, 0));
			}

			g_free (symtab);

			simple_symtab = new BfdSymbolTable (this);
		}

		protected ArrayList GetSimpleSymbols ()
		{
			return simple_symbols;
		}

		bool dynlink_handler (Inferior inferior)
		{
			if (inferior.ReadInteger (rdebug_state_addr) != 0)
				return false;

			do_update_shlib_info (inferior, inferior);
			return false;
		}

		bool read_dynamic_info (Inferior inferior, ITargetMemoryAccess target)
		{
			if (initialized)
				return has_shlib_info;

			initialized = true;

			InternalSection section = GetSectionByName (".dynamic", false);
			if (section == null)
				return false;

			TargetAddress vma = new TargetAddress (target.AddressDomain, section.vma);

			int size = (int) section.size;
			byte[] dynamic = target.ReadBuffer (vma, size);

			TargetAddress debug_base;
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (dynamic, 0, data, size);
				long base_ptr = bfd_glue_elfi386_locate_base (bfd, data, size);
				if (base_ptr == 0)
					return false;
				debug_base = new TargetAddress (target.AddressDomain, base_ptr);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}

			int the_size = 2 * target.TargetLongIntegerSize +
				3 * target.TargetAddressSize;

			TargetBlob blob = target.ReadMemory (debug_base, the_size);
			TargetReader reader = new TargetReader (blob.Contents, target);
			if (reader.ReadLongInteger () != 1)
				return false;

			first_link_map = reader.ReadAddress ();
			dynlink_breakpoint = reader.ReadAddress ();

			rdebug_state_addr = debug_base + reader.Offset;

			if (reader.ReadLongInteger () != 0)
				return false;

			if (inferior != null)
				inferior.BreakpointManager.InsertBreakpoint (
					inferior, new DynlinkBreakpoint (this), dynlink_breakpoint);

			has_shlib_info = true;
			return true;
		}

		public void UpdateSharedLibraryInfo (Inferior inferior, ITargetMemoryAccess target)
		{
			// This fails if it's a statically linked executable.
			try {
				if (!read_dynamic_info (inferior, target))
					return;
			} catch (Exception ex) {
				Console.WriteLine ("UPDATE SHLIB INFO #1: {0}", ex);
				return;
			}

			do_update_shlib_info (inferior, target);
		}

		void do_update_shlib_info (Inferior inferior, ITargetMemoryAccess target)
		{
			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
				int the_size = 4 * target.TargetAddressSize;
				TargetBlob blob = target.ReadMemory (map, the_size);
				TargetReader map_reader = new TargetReader (blob.Contents, target);

				TargetAddress l_addr = map_reader.ReadAddress ();
				TargetAddress l_name = map_reader.ReadAddress ();
				map_reader.ReadAddress ();

				string name;
				try {
					name = target.ReadString (l_name);
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

				Bfd bfd = container [name];
				if (bfd != null) {
					if (!bfd.IsLoaded)
						bfd.module_loaded (inferior, l_addr);
					continue;
				}

				bfd = container.AddFile (target, name, l_addr, module.StepInto, true);
				bfd.module_loaded (inferior, l_addr);
			}
		}

		public Bfd MainBfd {
			get {
				return main_bfd;
			}
		}

		public TargetAddress BaseAddress {
			get {
				return base_address;
			}
		}

		public Architecture Architecture {
			get {
				return arch;
			}
		}

		public BfdContainer BfdContainer {
			get {
				return container;
			}
		}

		public TargetAddress GetAddress (long address)
		{
			if (BaseAddress.IsNull)
				return new TargetAddress (
					info.AddressDomain, address);
			else
				return new TargetAddress (
					info.AddressDomain, BaseAddress.Address + address);
		}

		public ITargetInfo TargetInfo {
			get {
				return info;
			}
		}

		public string Name {
			get {
				return filename;
			}
		}

		public override string FullName {
			get {
				return filename;
			}
		}

		public string FileName {
			get {
				return filename;
			}
		}

		public string Target {
			get {
				return target;
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

				return bfd_glue_core_file_failing_command (bfd);
			}
		}

		public override Language Language {
			get {
				return container.NativeLanguage;
			}
		}

		internal override ILanguageBackend LanguageBackend {
			get {
				return this;
			}
		}

		internal DwarfReader DwarfReader {
			get { return dwarf; }
		}

		public override bool SymbolsLoaded {
			get { return (dwarf != null); }
		}

		public override SourceFile[] Sources {
			get {
				if (dwarf != null)
					return dwarf.Sources;

				throw new InvalidOperationException ();
			}
		}

		public override SourceMethod[] GetMethods (SourceFile file)
		{
			if (dwarf != null)
				return dwarf.GetMethods (file);

			throw new InvalidOperationException ();
		}

		public override Method GetMethod (int domain, long handle)
		{
			if (dwarf != null)
				return dwarf.GetMethod (handle);

			throw new InvalidOperationException ();
		}

		public override SourceMethod FindMethod (string name)
		{
			if (dwarf != null)
				return dwarf.FindMethod (name);

			return null;
		}

		public override ISymbolTable SymbolTable {
			get {
				if (dwarf != null)
					return dwarf.SymbolTable;
				else
					throw new InvalidOperationException ();
			}
		}

		public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (simple_symtab != null)
				return simple_symtab.SimpleLookup (address, exact_match);

			return null;
		}

		public TargetAddress EntryPoint {
			get { return entry_point; }
		}

		void create_frame_reader ()
		{
			long vma_base = base_address.IsNull ? 0 : base_address.Address;
			InternalSection section = GetSectionByName (".debug_frame", false);
			if (section != null) {
				byte[] contents = GetSectionContents (
					new IntPtr (section.section), false);
				TargetBlob blob = new TargetBlob (contents, info);
				frame_reader = new DwarfFrameReader (
					this, blob, vma_base + section.vma, false);
			}

			section = GetSectionByName (".eh_frame", false);
			if (section != null) {
				byte[] contents = GetSectionContents (
					new IntPtr (section.section), false);
				TargetBlob blob = new TargetBlob (contents, info);
				eh_frame_reader = new DwarfFrameReader (
					this, blob, vma_base + section.vma, true);
			}
		}

		void load_dwarf ()
		{
			if (dwarf_loaded)
				return;

			try {
				create_frame_reader ();
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF frame info from " +
						   "symbol file `{0}': {1}", FileName, ex);
			}

			if (!has_debugging_info)
				return;

			try {
				dwarf = new DwarfReader (
					this, module, container.Process.SourceFileFactory);
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				has_debugging_info = false;
				return;
			}

			dwarf_loaded = true;

			if (dwarf != null) {
				has_debugging_info = true;
				module.OnSymbolsLoadedEvent ();
			}
		}

		void unload_dwarf ()
		{
			if (!dwarf_loaded || !has_debugging_info)
				return;

			frame_reader = null;
			eh_frame_reader = null;

			dwarf_loaded = false;
			if (dwarf != null) {
				dwarf = null;
				module.OnSymbolsUnLoadedEvent ();
			}
		}

		internal override void OnModuleChanged ()
		{
			if (module.LoadSymbols) {
				load_dwarf ();
			} else {
				unload_dwarf ();
			}
		}

		internal BfdDisassembler GetDisassembler (ITargetMemoryAccess memory)
		{
			IntPtr dis = disassembler (bfd);

			IntPtr info = bfd_glue_init_disassembler (bfd);

			return new BfdDisassembler (
				container.Process.SymbolTableManager, memory, dis, info);
		}

		public TargetAddress this [string name] {
			get {
				if (symbols == null)
					return TargetAddress.Null;

				if (symbols.Contains (name))
					return (TargetAddress) symbols [name];

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
					info.AddressDomain, section.vma);
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
				throw new SymbolTableException ("Can't get bfd section {0}", name);

			byte[] contents = GetSectionContents (section, raw_section);
			if (contents == null)
				throw new SymbolTableException ("Can't get bfd section {0}", name);
			return contents;
		}

		public TargetReader GetSectionReader (string name, bool raw_section)
		{
			byte[] contents = GetSectionContents (name, raw_section);
			if (contents == null)
				throw new SymbolTableException ("Can't get bfd section {0}", name);
			return new TargetReader (contents, info);
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

		public bool HasSection (string name)
		{
			return GetSectionByName (name, false) != null;
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
				return module;
			}
		}

		public override bool HasDebuggingInfo {
			get {
				return has_debugging_info;
			}
		}

		public bool IsLoaded {
			get {
				return (main_bfd == null) || is_loaded || !base_address.IsNull;
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

		TargetAddress ILanguageBackend.RuntimeInvokeFunc {
			get {
				throw new InvalidOperationException ();
			}
		}

		TargetAddress ILanguageBackend.CompileMethodFunc {
			get {
				return TargetAddress.Null;
			}
		}

		TargetAddress ILanguageBackend.GetVirtualMethodFunc {
			get {
				throw new InvalidOperationException ();
			}
		}

		TargetAddress ILanguageBackend.GetBoxedObjectFunc {
			get {
				throw new InvalidOperationException ();
			}
		}

		TargetAddress ILanguageBackend.GetTrampolineAddress (ITargetMemoryAccess memory,
								     TargetAddress address,
								     out bool is_start)
		{
			return GetTrampoline (memory, address, out is_start);
		}

		SourceMethod ILanguageBackend.GetTrampoline (ITargetMemoryAccess memory,
							     TargetAddress address)
		{
			return null;
		}

		void ILanguageBackend.Notification (Inferior inferior, NotificationType type,
						    TargetAddress data, long arg)
		{ }

		public TargetAddress GetTrampoline (ITargetMemoryAccess memory,
						    TargetAddress address, out bool is_start)
		{
			if (!has_got || (address < plt_start) || (address > plt_end)) {
				is_start = false;
				return TargetAddress.Null;
			}

			int insn_size;
			TargetAddress jmp_target = arch.GetJumpTarget (memory, address, out insn_size);
			if (jmp_target.IsNull) {
				is_start = false;
				return TargetAddress.Null;
			}

			TargetAddress method = memory.ReadAddress (jmp_target);

			if (method != address + 6) {
				is_start = false;
				return method;
			}

			is_start = true;
			return memory.ReadAddress (got_start + 2 * memory.TargetAddressSize);
		}

		internal override StackFrame UnwindStack (StackFrame frame, ITargetMemoryAccess memory)
		{
			if ((frame.TargetAddress < StartAddress) || (frame.TargetAddress > EndAddress))
				return null;

			StackFrame new_frame;
			try {
				new_frame = arch.TrySpecialUnwind (frame, memory);
				if (new_frame != null)
					return new_frame;
			} catch {
			}

			try {
				if (frame_reader != null) {
					new_frame = frame_reader.UnwindStack (frame, memory, arch);
					if (new_frame != null)
						return new_frame;
				}

				if (eh_frame_reader != null) {
					new_frame = eh_frame_reader.UnwindStack (frame, memory, arch);
					if (new_frame != null)
						return new_frame;
				}
			} catch {
				return null;
			}

			return null;
		}

		public void ReadTypes ()
		{
			if (dwarf != null)
				dwarf.ReadTypes ();
		}

		internal override IDisposable RegisterLoadHandler (Thread target,
								   SourceMethod method,
								   MethodLoadedHandler handler,
								   object user_data)
		{
			LoadHandlerData data = new LoadHandlerData (
				this, method, handler, user_data);

			load_handlers.Add (data, true);
			return data;
		}

		protected void UnRegisterLoadHandler (LoadHandlerData data)
		{
			load_handlers.Remove (data);
		}

		void module_loaded (Inferior inferior, TargetAddress address)
		{
			this.base_address = address;

			if ((inferior != null) && symbols.Contains ("__libc_pthread_functions")) {
				TargetAddress vtable = (TargetAddress)
					symbols ["__libc_pthread_functions"];

				/*
				 * Big big hack to allow debugging gnome-vfs:
				 * We intercept any calls to __nptl_setxid() and make it
				 * return 0.  This is safe to do since we do not allow
				 * debugging setuid programs or running as root, so setxid()
				 * will always be a no-op anyways.
				 */

				TargetAddress nptl_setxid = inferior.ReadAddress (
					vtable + 51 * info.TargetAddressSize);

				if (!nptl_setxid.IsNull)
					inferior.BreakpointManager.InsertBreakpoint (
						inferior, new SetXidBreakpoint (this), nptl_setxid);
			}

			if (dwarf != null) {
				dwarf.ModuleLoaded ();

				has_debugging_info = true;
				module.OnSymbolsLoadedEvent ();
			}

			foreach (LoadHandlerData data in load_handlers.Keys)
				data.Handler (inferior, data.Method, data.UserData);
		}

		protected sealed class LoadHandlerData : IDisposable
		{
			public readonly Bfd Bfd;
			public readonly SourceMethod Method;
			public readonly MethodLoadedHandler Handler;
			public readonly object UserData;

			public LoadHandlerData (Bfd bfd, SourceMethod method,
						MethodLoadedHandler handler,
						object user_data)
			{
				this.Bfd = bfd;
				this.Method = method;
				this.Handler = handler;
				this.UserData = user_data;
			}

			private bool disposed = false;

			private void Dispose (bool disposing)
			{
				if (!this.disposed) {
					if (disposing) {
						Bfd.UnRegisterLoadHandler (this);
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

			~LoadHandlerData ()
			{
				Dispose (false);
			}
		}

		//
		// The BFD symbol table.
		//

		private class BfdSymbolTable
		{
			Bfd bfd;
			Symbol[] list;
			TargetAddress start, end;

			public BfdSymbolTable (Bfd bfd)
			{
				this.bfd = bfd;
				this.start = bfd.StartAddress;
				this.end = bfd.EndAddress;
			}

			public Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				if ((address < start) || (address >= end))
					return null;

				if (list == null) {
					ArrayList the_list = bfd.GetSimpleSymbols ();
					the_list.Sort ();

					list = new Symbol [the_list.Count];
					the_list.CopyTo (list);
				}

				for (int i = list.Length - 1; i >= 0; i--) {
					Symbol entry = list [i];

					if (address < entry.Address)
						continue;

					long offset = address - entry.Address;
					if (offset == 0) {
						while (i > 0) {
							Symbol n_entry = list [--i];

							if (n_entry.Address == entry.Address) 
								entry = n_entry;
							else
								break;
						}

						return new Symbol (entry.Name, address, 0);
					} else if (exact_match)
						return null;
					else
						return new Symbol (
							entry.Name, address - offset,
							(int) offset);
				}

				return null;
			}
		}

		[Serializable]
		protected class SetXidBreakpoint : Breakpoint
		{
			protected readonly Bfd bfd;

			public SetXidBreakpoint (Bfd bfd)
				: base ("setxid", null)
			{
				this.bfd = bfd;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				bfd.arch.Hack_ReturnNull (inferior);
				remain_stopped = false;
				return true;
			}

			public override Breakpoint Clone ()
			{
				return new SetXidBreakpoint (bfd);
			}
		}

		[Serializable]
		protected class DynlinkBreakpoint : Breakpoint
		{
			protected readonly Bfd bfd;

			public DynlinkBreakpoint (Bfd bfd)
				: base ("dynlink", null)
			{
				this.bfd = bfd;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				bfd.dynlink_handler (inferior);
				remain_stopped = false;
				return true;
			}

			public override Breakpoint Clone ()
			{
				return new DynlinkBreakpoint (bfd);
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
				this.disposed = true;

				// Release unmanaged resources
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
