using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Architecture
{
	internal delegate void BfdDisposedHandler (Bfd bfd);
 
	internal class Bfd : Module, ISymbolContainer, ILanguageBackend,
		IDisposable
	{
		IntPtr bfd;
		protected BfdContainer container;
		protected DebuggerBackend backend;
		protected ITargetMemoryInfo info;
		protected Bfd core_file_bfd;
		protected Bfd main_bfd;
		protected IArchitecture arch;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;
		TargetAddress entry_point = TargetAddress.Null;
		bool is_main_module;
		bool is_loaded;
		int dynlink_breakpoint_id;
		Hashtable load_handlers;
		Hashtable symbols;
		ArrayList simple_symbols;
		ISimpleSymbolTable simple_symtab;
		DwarfReader dwarf;
		DwarfFrameReader frame_reader, eh_frame_reader;
		StabsReader stabs;
		bool dwarf_loaded;
		bool stabs_loaded;
		bool has_debugging_info;
		string filename, target;
		bool is_coredump;
		bool initialized;
		bool has_shlib_info;
		TargetAddress base_address, start_address, end_address;
		TargetAddress plt_start, plt_end, got_start;
		bool is_powerpc;
		bool use_stabs;
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
		extern static string bfd_core_file_failing_command (IntPtr bfd);

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

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out int is_function, out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (BfdContainer container, ITargetMemoryAccess memory,
			    ITargetMemoryInfo info, string filename, bool core_file,
			    TargetAddress base_address, bool is_main_module, bool is_loaded)
		{
			this.container = container;
			this.info = info;
			this.filename = filename;
			this.base_address = base_address;
			this.backend = container.DebuggerBackend;
			this.is_main_module = is_main_module;
			this.is_loaded = is_loaded;

			load_handlers = new Hashtable ();

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

			target = bfd_glue_get_target_name (bfd);
			if (target == "elf32-i386") {
				arch = new ArchitectureI386 ();

				InternalSection text = GetSectionByName (".text", true);

				if (!base_address.IsNull)
					start_address = new TargetAddress (
						info.GlobalAddressDomain,
						base_address.Address + text.vma);
				else
					start_address = new TargetAddress (
						info.GlobalAddressDomain, text.vma);
				end_address = start_address + text.size;

				read_bfd_symbols ();

				if (StabsReader.IsSupported (this)) {
					has_debugging_info = true;
					use_stabs = true;
				} else if (DwarfReader.IsSupported (this))
					has_debugging_info = true;

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
			} else if (target == "mach-o-be") {
				arch = new ArchitecturePowerPC ();

				use_stabs = true;
				is_powerpc = true;

				InternalSection text = GetSectionByName (
					"LC_SEGMENT.__TEXT.__text", true);

				start_address = new TargetAddress (
					info.GlobalAddressDomain, text.vma);
				end_address = start_address + text.size;

				has_debugging_info = StabsReader.IsSupported (this);
			} else
				throw new SymbolTableException (
					"Symbol file {0} has unknown target architecture {1}",
					filename, target);

			if (is_powerpc) {
				load_stabs ();

				if (stabs != null) {
					simple_symtab = stabs;
					entry_point = stabs.EntryPoint;
				} else
					entry_point = SimpleLookup ("_main");
			} else {
				if (use_stabs)
					load_stabs ();

				entry_point = SimpleLookup ("main");
			}

			backend.ModuleManager.AddModule (this);

			OnModuleChangedEvent ();
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
					info.GlobalAddressDomain,
					base_address.Address + address);
				if (is_function != 0)
					symbols.Add (name, relocated);
				else if (is_main_module && name.StartsWith ("MONO_DEBUGGER__"))
					symbols.Add (name, relocated);
				else if (name.StartsWith ("__pthread_"))
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
					info.GlobalAddressDomain,
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

		bool dynlink_handler (StackFrame frame, ITargetAccess target, int index,
				      object user_data)
		{
			if (target.ReadInteger (rdebug_state_addr) != 0)
				return false;

			do_update_shlib_info (target);
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
				false, null);

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

			do_update_shlib_info (inferior);
		}

		void do_update_shlib_info (ITargetAccess target)
		{
			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
				ITargetMemoryReader map_reader = target.ReadMemory (map, 16);

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
						bfd.module_loaded (target, l_addr);
					continue;
				}

				bfd = container.AddFile (
					target, name, StepInto, l_addr, null, false, true);
				bfd.module_loaded (target, l_addr);
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

		public IArchitecture Architecture {
			get {
				return arch;
			}
		}

		public TargetAddress GetAddress (long address)
		{
			if (BaseAddress.IsNull)
				return new TargetAddress (
					info.GlobalAddressDomain, address);
			else
				return new TargetAddress (
					info.GlobalAddressDomain, BaseAddress.Address + address);
		}

		public ITargetInfo TargetInfo {
			get {
				return info;
			}
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
			get { return (dwarf != null) || (stabs != null); }
		}

		public override ISymbolFile SymbolFile {
			get {
				if (dwarf != null)
					return dwarf;
				else if (stabs != null)
					return stabs;
				else
					return null;
			}
		}

		public override ISymbolTable SymbolTable {
			get {
				if (dwarf != null)
					return dwarf.SymbolTable;
				else if (stabs != null)
					return stabs;
				else
					throw new InvalidOperationException ();
			}
		}

		public override TargetAddress SimpleLookup (string name)
		{
			return this [name];
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
				TargetBlob blob = new TargetBlob (contents);
				frame_reader = new DwarfFrameReader (
					this, blob, vma_base + section.vma, false);
			}

			section = GetSectionByName (".eh_frame", false);
			if (section != null) {
				byte[] contents = GetSectionContents (
					new IntPtr (section.section), false);
				TargetBlob blob = new TargetBlob (contents);
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
				dwarf = new DwarfReader (this, this, backend.SourceFileFactory);
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				has_debugging_info = false;
				return;
			}

			dwarf_loaded = true;

			if (dwarf != null) {
				has_debugging_info = true;
				OnSymbolsLoadedEvent ();
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
				OnSymbolsUnLoadedEvent ();
			}
		}

		void load_stabs ()
		{
			if (stabs_loaded)
				return;

			try {
				stabs = new StabsReader (this, this, backend.SourceFileFactory);
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read STABS debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				has_debugging_info = false;
			}

			stabs_loaded = true;

			if (stabs != null) {
				has_debugging_info = true;
				OnSymbolsLoadedEvent ();
			}
		}

		void unload_stabs ()
		{
			if (!stabs_loaded)
				return;

			stabs_loaded = false;
			if (stabs != null) {
				stabs = null;
				OnSymbolsUnLoadedEvent ();
			}
		}

		protected override void OnModuleChangedEvent ()
		{
			if (LoadSymbols) {
				if (use_stabs)
					load_stabs ();
				else
					load_dwarf ();
			} else {
				if (use_stabs)
					unload_stabs ();
				else
					unload_dwarf ();
			}

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
					info.GlobalAddressDomain, section.vma);
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
				return this;
			}
		}

		public override bool HasDebuggingInfo {
			get {
				return has_debugging_info;
			}
		}

		public override bool IsLoaded {
			get {
				return is_main_module || is_loaded || !base_address.IsNull;
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

			ITargetMemoryReader reader = memory.ReadMemory (address, 10);

			byte opcode = reader.ReadByte ();
			byte opcode2 = reader.ReadByte ();

			TargetAddress jmp_target;
			if ((opcode == 0xff) && (opcode2 == 0x25)) {
				jmp_target = reader.ReadAddress ();
			} else if ((opcode == 0xff) && (opcode2 == 0xa3)) {
				int offset = reader.BinaryReader.ReadInt32 ();
				Registers regs = memory.GetRegisters ();
				long ebx = regs [(int) I386Register.EBX].Value;

				jmp_target = new TargetAddress (
					memory.AddressDomain, ebx + offset);
			} else {
				is_start = false;
				return TargetAddress.Null;
			}

			TargetAddress method = memory.ReadGlobalAddress (jmp_target);

			if (method != address + 6) {
				is_start = false;
				return method;
			}

			is_start = true;
			return memory.ReadGlobalAddress (got_start + 8);
		}

		internal override SimpleStackFrame UnwindStack (SimpleStackFrame frame,
								ITargetMemoryAccess memory)
		{
			if (frame_reader != null) {
				SimpleStackFrame new_frame = frame_reader.UnwindStack (
					frame, memory, arch);
				if (new_frame != null)
					return new_frame;
			}
			if (eh_frame_reader != null) {
				SimpleStackFrame new_frame = eh_frame_reader.UnwindStack (
					frame, memory, arch);
				if (new_frame != null)
					return new_frame;
			}
			return null;
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			if (dwarf != null)
				return dwarf.LookupType (frame, name);

			return null;
		}

		internal override IDisposable RegisterLoadHandler (Process process,
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

		void module_loaded (ITargetAccess target, TargetAddress address)
		{
			this.base_address = address;

			if (dwarf != null) {
				dwarf.ModuleLoaded ();

				has_debugging_info = true;
				OnSymbolsLoadedEvent ();
			}

			foreach (LoadHandlerData data in load_handlers.Keys)
				data.Handler (target, data.Method, data.UserData);
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

		private class BfdSymbolTable : ISimpleSymbolTable
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
