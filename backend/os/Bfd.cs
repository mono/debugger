using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal delegate void BfdDisposedHandler (Bfd bfd);
 
	internal class Bfd : NativeExecutableReader, ISymbolContainer
	{
		IntPtr bfd;
		protected Module module;
		protected OperatingSystemBackend os;
		protected TargetMemoryInfo info;
		protected BfdSymbolFile symfile;
		TargetAddress entry_point = TargetAddress.Null;
		bool is_loaded;
		Hashtable symbols;
		Hashtable local_symbols;
		ArrayList simple_symbols;
		BfdSymbolTable simple_symtab;
		DwarfReader dwarf;
		DwarfFrameReader frame_reader, eh_frame_reader;
		bool dwarf_loaded;
		bool frames_loaded;
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

		internal class Section
		{
			public readonly Bfd bfd;
			public readonly IntPtr section;
			public readonly string name;
			public readonly long vma;
			public readonly long size;
			public readonly SectionFlags flags;
			public readonly ObjectCache contents;

			internal Section (Bfd bfd, IntPtr section)
			{
				this.bfd = bfd;
				this.section = section;

				this.name = bfd_glue_get_section_name (section);
				this.vma = bfd_glue_get_section_vma (section);
				this.size = bfd_glue_get_section_size (section);
				this.flags = bfd_glue_get_section_flags (section);

				contents = new ObjectCache (
					new ObjectCacheFunc (get_section_contents), section, 5);
			}

			object get_section_contents (object user_data)
			{
				byte[] data = bfd.GetSectionContents (section);
				if (data == null)
					throw new SymbolTableException ("Can't get bfd section {0}", name);
				return new TargetReader (data, bfd.info);
			}

			public TargetReader GetReader (TargetAddress address)
			{
				TargetReader reader = (TargetReader) contents.Data;
				reader.Offset = address.Address - bfd.BaseAddress.Address - vma;
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
		extern static string bfd_glue_get_errormsg ();

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
		extern static bool bfd_glue_check_format_archive (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_openr_next_archived_file (IntPtr archive, IntPtr last);

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_get_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_get_dynamic_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("monodebuggerserver")]
		extern static bool bfd_glue_get_section_contents (IntPtr bfd, IntPtr section, IntPtr data, int size);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_get_first_section (IntPtr bfd);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_get_next_section (IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static long bfd_glue_get_section_vma (IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_get_section_name (IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_get_section_size (IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static SectionFlags bfd_glue_get_section_flags (IntPtr section);

		[DllImport("monodebuggerserver")]
		extern static long bfd_glue_elfi386_locate_base (IntPtr bfd, IntPtr data, int size);

		[DllImport("monodebuggerserver")]
		extern static long bfd_glue_get_start_address (IntPtr bfd);

		[DllImport("libglib-2.0-0.dll")]
		extern static void g_free (IntPtr data);

		[DllImport("monodebuggerserver")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out int is_function, out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (OperatingSystemBackend os, TargetMemoryInfo info, string filename,
			    TargetAddress base_address, bool is_loaded)
		{
			this.os = os;
			this.info = info;
			this.filename = filename;
			this.base_address = base_address;
			this.is_loaded = is_loaded;

			this.symfile = new BfdSymbolFile (this);

			bfd = bfd_glue_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new SymbolTableException ("Can't read symbol file: {0}", filename);

			if (bfd_glue_check_format_archive (bfd))
			{
				IntPtr archive = bfd;
				bfd = IntPtr.Zero;
				
				while (true) {
					bfd = bfd_glue_openr_next_archived_file (archive, bfd);
					if(bfd == IntPtr.Zero)
						throw new SymbolTableException ("Can't read symbol file: {0}", filename);
					
					if (bfd_glue_check_format_object(bfd))
					{
						/*
						 * At this point, just check for mach-o-le (OS X X86 binary).
						 * When we want to support other architctures in fat binarys,
						 * we need to somehow get the correct target string for the
						 * process, and chech against that.
						 */
						if (bfd_glue_get_target_name(bfd) == "mach-o-le")
							break;
					}
				}
			}
			
			if (bfd_glue_check_format_object (bfd))
				is_coredump = false;
			else if (bfd_glue_check_format_core (bfd))
				is_coredump = true;
			else
				throw new SymbolTableException ("Not an object file: {0}", filename);

			target = bfd_glue_get_target_name (bfd);
			if ((target == "elf32-i386") || (target == "elf64-x86-64")) {
				if (!is_coredump) {
					Section text = GetSectionByName (".text", false);
					Section bss = GetSectionByName (".bss", false);

					if ((text != null) && (bss != null)) {
						if (!base_address.IsNull)
							start_address = new TargetAddress (
								info.AddressDomain,
								base_address.Address + text.vma);
						else
							start_address = new TargetAddress (
								info.AddressDomain, text.vma);

						if (!base_address.IsNull)
							end_address = new TargetAddress (
								info.AddressDomain,
								base_address.Address + bss.vma + bss.size);
						else
							end_address = new TargetAddress (
								info.AddressDomain, bss.vma + bss.size);
					}
				}

				read_bfd_symbols ();

				if (DwarfReader.IsSupported (this))
					has_debugging_info = true;

				Section plt_section = GetSectionByName (".plt", false);
				Section got_section = GetSectionByName (".got", false);
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
			} else if (target == "mach-o-le") {
				if (!is_coredump) {
					read_sections ();
					long start = 0xffffffff;
					long end = 0;
					foreach (Section section in sections) {
						long relocated = base_address.Address + section.vma;
		
						if (relocated < start)
							start = relocated;
						if (relocated + section.size > end)
							end = relocated + section.size;
					}
				
					start_address = new TargetAddress (info.AddressDomain, start);
					end_address = new TargetAddress (info.AddressDomain, end);
				}

				read_bfd_symbols ();

				if (DwarfReader.IsSupported (this))
					has_debugging_info = true;

				has_got = false;
			} else
				throw new SymbolTableException (
					"Symbol file {0} has unknown target architecture {1}",
					filename, target);

			long entry_point_addr = bfd_glue_get_start_address (bfd);
			entry_point = entry_point_addr != 0 ? new TargetAddress (info.AddressDomain, entry_point_addr) : TargetAddress.Null;

			module = os.Process.Session.GetModule (filename);
			if (module == null) {
				module = os.Process.Session.CreateModule (filename, symfile);
				OnModuleChanged ();
			} else {
				module.LoadModule (symfile);
			}

			os.Process.SymbolTableManager.AddSymbolFile (symfile);
		}

		public Bfd OpenCoreFile (string core_file)
		{
			Bfd core = new Bfd (os, info, core_file, TargetAddress.Null, true);
			core.is_coredump = true;
			return core;
		}

		void read_bfd_symbols ()
		{
			IntPtr symtab;
			int num_symbols = bfd_glue_get_symbols (bfd, out symtab);

			symbols = new Hashtable ();
			local_symbols = new Hashtable ();
			simple_symbols = new ArrayList ();

			bool is_mach = (bfd_glue_get_target_name (bfd) == "mach-o-le");

			for (int i = 0; i < num_symbols; i++) {
				string name;
				long address;
				int is_function;

				name = bfd_glue_get_symbol (bfd, symtab, i, out is_function, out address);
				if (name == null)
					continue;

				if(is_mach) {
					if(name[0] == '_')
						name = name.Substring(1);
				}

				TargetAddress relocated = new TargetAddress (
					info.AddressDomain, base_address.Address + address);
				if (is_function != 0 && !symbols.Contains (name))
					symbols.Add (name, relocated);
				else if (name.StartsWith ("MONO_DEBUGGER__") && !symbols.Contains (name))
					symbols.Add (name, relocated);
				else if (!local_symbols.Contains (name))
					local_symbols.Add (name, relocated);

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

		internal TargetAddress ReadDynamicInfo (Inferior inferior)
		{
			Section section = GetSectionByName (".dynamic", false);
			if (section == null)
				return TargetAddress.Null;

			TargetAddress vma = new TargetAddress (info.AddressDomain, section.vma);

			int size = (int) section.size;
			byte[] dynamic = inferior.ReadBuffer (vma, size);

			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (dynamic, 0, data, size);
				long base_ptr = bfd_glue_elfi386_locate_base (bfd, data, size);
				if (base_ptr == 0)
					return TargetAddress.Null;
				else
					return new TargetAddress (info.AddressDomain, base_ptr);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public TargetAddress BaseAddress {
			get {
				return base_address;
			}
		}

		public Architecture Architecture {
			get {
				return os.Process.Architecture;
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

		public TargetMemoryInfo TargetMemoryInfo {
			get {
				return info;
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

		public ProcessServant Process {
			get { return os.Process; }
		}

		public NativeLanguage NativeLanguage {
			get { return os.Process.NativeLanguage; }
		}

		internal DwarfReader DwarfReader {
			get { return dwarf; }
		}

		protected bool SymbolsLoaded {
			get { return (dwarf != null); }
		}

		protected SourceFile[] Sources {
			get {
				if (dwarf != null)
					return dwarf.Sources;

				throw new InvalidOperationException ();
			}
		}

		protected MethodSource[] GetMethods (SourceFile file)
		{
			if (dwarf != null)
				return dwarf.GetMethods (file);

			throw new InvalidOperationException ();
		}

		protected MethodSource FindMethod (string name)
		{
			if (dwarf != null)
				return dwarf.FindMethod (name);

			return null;
		}

		protected ISymbolTable SymbolTable {
			get {
				if (dwarf != null)
					return dwarf.SymbolTable;
				else
					throw new InvalidOperationException ();
			}
		}

		protected Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (simple_symtab != null)
				return simple_symtab.SimpleLookup (address, exact_match);

			return null;
		}

		public override TargetAddress EntryPoint {
			get { return entry_point; }
		}

		void create_frame_reader ()
		{
			long vma_base = base_address.IsNull ? 0 : base_address.Address;
			Section section = GetSectionByName (".debug_frame", false);
			if (section != null) {
				byte[] contents = GetSectionContents (section.section);
				TargetBlob blob = new TargetBlob (contents, info);
				frame_reader = new DwarfFrameReader (
					this, blob, vma_base + section.vma, false);
			}

			section = GetSectionByName (".eh_frame", false);
			if (section != null) {
				byte[] contents = GetSectionContents (section.section);
				TargetBlob blob = new TargetBlob (contents, info);
				eh_frame_reader = new DwarfFrameReader (
					this, blob, vma_base + section.vma, true);
			}
		}

		void load_frames ()
		{
			if (frames_loaded)
				return;

			try {
				create_frame_reader ();
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF frame info from " +
						   "symbol file `{0}': {1}", FileName, ex);
			}

			frames_loaded = true;
		}

		void load_dwarf ()
		{
			if (dwarf_loaded || !has_debugging_info)
				return;

			try {
				dwarf = new DwarfReader (this, module);
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				has_debugging_info = false;
				return;
			}

			dwarf_loaded = true;

			if (dwarf != null)
				has_debugging_info = true;
		}

		void unload_dwarf ()
		{
			if (!dwarf_loaded || !has_debugging_info)
				return;

			dwarf_loaded = false;
			dwarf = null;
		}

		protected void OnModuleChanged ()
		{
			load_frames ();

			if (module.LoadSymbols) {
				load_dwarf ();
			} else {
				unload_dwarf ();
			}
		}

		public override TargetAddress LookupSymbol (string name)
		{
			if (symbols == null)
				return TargetAddress.Null;

			if (symbols.Contains (name))
				return (TargetAddress) symbols [name];

			return TargetAddress.Null;
		}

		public override TargetAddress LookupLocalSymbol (string name)
		{
			if (local_symbols == null)
				return TargetAddress.Null;

			if (local_symbols.Contains (name))
				return (TargetAddress) local_symbols [name];

			return TargetAddress.Null;
		}

		internal Section FindSection (long address)
		{
			read_sections ();
			foreach (Section section in sections) {
				long relocated = base_address.Address + section.vma;

				if ((address < relocated) || (address >= relocated + section.size))
					continue;

					return section;
			}

			return null;
		}

		internal Section this [long address] {
			get {
				Section section = FindSection (address);
				if (section != null)
					return section;

				throw new SymbolTableException (
					"No section in file {1} contains address {0:x}.", address,
					filename);
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

		public TargetReader GetReader (TargetAddress address, bool may_fail)
		{
			Section section = FindSection (address.Address);
			if (section != null)
				return section.GetReader (address);

			if (may_fail)
				return null;

			throw new SymbolTableException (
				"No section in file {1} contains address {0:x}.", address,
				filename);
		}

		public override TargetReader GetReader (TargetAddress address)
		{
			Section section = FindSection (address.Address);
			if (section != null)
				return section.GetReader (address);

			return null;
		}

		public override TargetAddress GetSectionAddress (string name)
		{
			IntPtr section;

			section = bfd_get_section_by_name (bfd, name);
			if (section == IntPtr.Zero)
				return TargetAddress.Null;

			long vma = bfd_glue_get_section_vma (section);
			return GetAddress (vma);
		}

		public byte[] GetSectionContents (string name)
		{
			IntPtr section;

			section = bfd_get_section_by_name (bfd, name);
			if (section == IntPtr.Zero)
				throw new SymbolTableException ("Can't find bfd section {0}", name);

			byte[] contents = GetSectionContents (section);
			if (contents == null)
				throw new SymbolTableException ("Can't read bfd section {0}", name);
			return contents;
		}

		public TargetReader GetSectionReader (string name)
		{
			byte[] contents = GetSectionContents (name);
			if (contents == null)
				throw new SymbolTableException ("Can't read bfd section {0}", name);
			return new TargetReader (contents, info);
		}

		byte[] GetSectionContents (IntPtr section)
		{
			int size = bfd_glue_get_section_size (section);
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				if (!bfd_glue_get_section_contents (bfd, section, data, size)) {
					string error = bfd_glue_get_errormsg ();
					string name = bfd_glue_get_section_name (section);

					throw new SymbolTableException (
						"Can't read bfd section {0}: {1}", name, error);
				}
				byte[] retval = new byte [size];
				Marshal.Copy (data, retval, 0, size);
				return retval;
			} finally {
				Marshal.FreeHGlobal (data);
			}
		}

		public bool HasSection (string name)
		{
			return GetSectionByName (name, false) != null;
		}

		Section GetSectionByName (string name, bool throw_exc)
		{
			IntPtr section = bfd_get_section_by_name (bfd, name);
			if (section == IntPtr.Zero) {
				if (throw_exc)
					throw new SymbolTableException (
						"Can't get bfd section {0}", name);
				else
					return null;
			}

			return new Section (this, section);
		}

		bool has_sections = false;
		Section[] sections = null;

		protected void read_sections ()
		{
			if (has_sections)
				return;

			ArrayList list = new ArrayList ();
			IntPtr asection = bfd_glue_get_first_section (bfd);
			while (asection != IntPtr.Zero) {
				Section section = new Section (this, asection);
				list.Add (section);

				asection = bfd_glue_get_next_section (asection);
			}

			sections = new Section [list.Count];
			list.CopyTo (sections, 0);

			has_sections = true;
		}

		public override Module Module {
			get {
				return module;
			}
		}

		protected bool HasDebuggingInfo {
			get {
				return has_debugging_info;
			}
		}

		public bool IsLoaded {
			get {
				return is_loaded || !base_address.IsNull;
			}
		}

		//
		// ISymbolContainer
		//

		public bool IsContinuous {
			get {
				return !is_coredump && !end_address.IsNull;
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

		internal bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
					     out TargetAddress trampoline, out bool is_start)
		{
			if (!has_got || (address < plt_start) || (address > plt_end)) {
				is_start = false;
				trampoline = TargetAddress.Null;
				return false;
			}

			Instruction target_insn = Architecture.ReadInstruction (memory, address);
			if ((target_insn == null) || !target_insn.HasInstructionSize ||
			    ((target_insn.InstructionType != Instruction.Type.Jump) &&
			     (target_insn.InstructionType != Instruction.Type.IndirectJump))) {
				is_start = false;
				trampoline = TargetAddress.Null;
				return false;
			}

			TargetAddress call_target = target_insn.GetEffectiveAddress (memory);
			if (call_target.IsNull) {
				is_start = false;
				trampoline = TargetAddress.Null;
				return false;
			}

			if (call_target != address + target_insn.InstructionSize) {
				is_start = false;
				trampoline = call_target;
				return true;
			}

			is_start = true;
			trampoline = memory.ReadAddress (got_start + 3 * info.TargetAddressSize);
			return true;
		}

		protected StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory)
		{
			if ((frame.TargetAddress < StartAddress) || (frame.TargetAddress > EndAddress))
				return null;

			StackFrame new_frame;
			try {
				new_frame = Architecture.TrySpecialUnwind (frame, memory);
				if (new_frame != null)
					return new_frame;
			} catch {
			}

			try {
				if (frame_reader != null) {
					new_frame = frame_reader.UnwindStack (frame, memory, Architecture);
					if (new_frame != null)
						return new_frame;
				}

				if (eh_frame_reader != null) {
					new_frame = eh_frame_reader.UnwindStack (frame, memory, Architecture);
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

		protected class BfdSymbolFile : SymbolFile
		{
			public readonly Bfd Bfd;

			public BfdSymbolFile (Bfd bfd)
			{
				this.Bfd = bfd;
			}

			public override bool IsNative {
				get { return true; }
			}

			public override string FullName {
				get { return Bfd.FileName; }
			}

			public override string CodeBase {
				get { return Bfd.FileName; }
			}

			public override Language Language {
				get { return Bfd.NativeLanguage; }
			}

			public override Module Module {
				get { return Bfd.Module; }
			}

			public override bool HasDebuggingInfo {
				get { return Bfd.HasDebuggingInfo; }
			}

			public override bool SymbolsLoaded {
				get { return Bfd.SymbolsLoaded; }
			}

			public override SourceFile[] Sources {
				get { return Bfd.Sources; }
			}

			public override MethodSource[] GetMethods (SourceFile file)
			{
				return Bfd.GetMethods (file);
			}

			public override MethodSource FindMethod (string name)
			{
				return Bfd.FindMethod (name);
			}

			public override ISymbolTable SymbolTable {
				get { return Bfd.SymbolTable; }
			}

			public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				return Bfd.SimpleLookup (address, exact_match);
			}

			internal override void OnModuleChanged ()
			{
				Bfd.OnModuleChanged ();
			}

			internal override StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory)
			{
				return Bfd.UnwindStack (frame, memory);
			}
		}

		//
		// The BFD symbol table.
		//

		private class BfdSymbolTable
		{
			Bfd bfd;
			Symbol[] list;

			public BfdSymbolTable (Bfd bfd)
			{
				this.bfd = bfd;
			}

			public Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				if (bfd.IsContinuous &&
				    ((address < bfd.StartAddress) || (address >= bfd.EndAddress)))
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

		protected override void DoDispose ()
		{
			bfd_close (bfd);
			bfd = IntPtr.Zero;
			base.DoDispose ();
		}
	}
}
