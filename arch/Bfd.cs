using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal delegate void BfdDisposedHandler (Bfd bfd);
 
	internal class Bfd : ISymbolContainer, IDisposable
	{
		IntPtr bfd;
		protected BfdContainer container;
		protected DebuggerBackend backend;
		protected ThreadManager thread_manager;
		protected IInferior inferior;
		protected Bfd core_file_bfd;
		protected Bfd main_bfd;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;
		int dynlink_breakpoint_id = -1;
		Hashtable symbols;
		Hashtable section_hash;
		DwarfReader dwarf;
		BfdModule module;
		string filename;
		bool is_coredump;
		bool initialized;
		bool has_shlib_info;
		TargetAddress base_address, end_address;

		[Flags]
		internal enum SectionFlags {
			Load = 1,
			Alloc = 2,
			ReadOnly = 4
		};

		internal struct InternalSection
		{
			public readonly int index;
			public readonly int flags;
			public readonly long vma;
			public readonly long size;
			public readonly long section;
		}

		public class Section
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
				return new TargetReader (data, bfd.inferior);
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

		[DllImport("libbfd")]
		extern static void bfd_init ();

		[DllImport("libbfd")]
		extern static IntPtr bfd_openr (string filename, string target);

		[DllImport("libbfd")]
		extern static bool bfd_close (IntPtr bfd);

		[DllImport("libbfd")]
		extern static IntPtr bfd_get_section_by_name (IntPtr bfd, string name);

		[DllImport("libbfd")]
		extern static string bfd_core_file_failing_command (IntPtr bfd);

		[DllImport("libopcodes")]
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
							  out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (BfdContainer container, IInferior inferior, string filename, bool core_file,
			    BfdModule module, TargetAddress base_address)
		{
			this.container = container;
			this.inferior = inferior;
			this.filename = filename;
			this.module = module;
			this.base_address = base_address;
			this.backend = inferior.DebuggerBackend;

			thread_manager = backend.ThreadManager;

			bfd = bfd_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new SymbolTableException ("Can't read symbol file: {0}", filename);

			section_hash = new Hashtable ();

			if (core_file) {
				if (!bfd_glue_check_format_core (bfd))
					throw new SymbolTableException ("Not a core file: {0}", filename);

				is_coredump = true;

				return;
			}

			if (!bfd_glue_check_format_object (bfd))
				throw new SymbolTableException ("Not an object file: {0}", filename);

			IntPtr symtab;
			int num_symbols = bfd_glue_get_symbols (bfd, out symtab);

			symbols = new Hashtable ();

			for (int i = 0; i < num_symbols; i++) {
				long address;
				string name =  bfd_glue_get_symbol (bfd, symtab, i, out address);
				if (name == null)
					continue;

				symbols.Add (name, base_address.Address + address);
			}

			g_free (symtab);

			if (!base_address.IsNull) {
				InternalSection bss = GetSectionByName (".bss");
				end_address = base_address + bss.vma;
				initialized = true;
			}
		}

		bool dynlink_handler (StackFrame frame, int index, object user_data)
		{
			if (inferior.ReadInteger (rdebug_state_addr) != 0)
				return false;

			UpdateSharedLibraryInfo ();
			return false;
		}

		bool read_dynamic_info ()
		{
			if (initialized)
				return has_shlib_info;

			initialized = true;

			InternalSection section = GetSectionByName (".dynamic");

			TargetAddress vma = new TargetAddress (inferior, section.vma);

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
				debug_base = new TargetAddress (inferior, base_ptr);
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

#if FALSE
			if (inferior.State != TargetState.CORE_FILE) {
				dynlink_breakpoint_id = backend.SingleSteppingEngine.InsertBreakpoint (
					dynlink_breakpoint, new BreakpointHitHandler (dynlink_handler),
					false, null);
			}
#endif

			has_shlib_info = true;
			return true;
		}

		public void UpdateSharedLibraryInfo ()
		{
			// This fails if it's a statically linked executable.
			try {
				if (!read_dynamic_info ())
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
				TargetAddress l_ld = map_reader.ReadAddress ();

				string name;
				try {
					name = inferior.ReadString (l_name);
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

				Bfd library_bfd = container.AddFile (
					inferior, name, module.StepInto, l_addr, null);
			}
		}

		public Bfd CoreFileBfd {
			get {
				return core_file_bfd;
			}

			set {
				core_file_bfd = value;
				if (core_file_bfd != null) {
					InternalSection text = GetSectionByName (".text");

					base_address = new TargetAddress (inferior, text.vma);
					end_address = new TargetAddress (inferior, text.vma + text.size);
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
				return new TargetAddress (thread_manager, address);
			else
				return new TargetAddress (thread_manager, BaseAddress.Address + address);
		}

		public void ReadDwarf ()
		{
			if (dwarf != null)
				return;

			try {
				dwarf = new DwarfReader (this, module);
			} catch (Exception e) {
				throw new SymbolTableException (
					"Symbol file {0} doesn't contain any DWARF 2 debugging info.",
					filename);
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

		public ISymbolTable SymbolTable {
			get {
				if (dwarf == null)
					return null;

				return dwarf.SymbolTable;
			}
		}

		public BfdDisassembler GetDisassembler (IInferior inferior)
		{
			IntPtr dis = disassembler (bfd);

			IntPtr info = bfd_glue_init_disassembler (bfd);

			return new BfdDisassembler (inferior, dis, info);
		}

		public TargetAddress this [string name] {
			get {
				if (symbols == null)
					return TargetAddress.Null;

				if (symbols.Contains (name))
					return new TargetAddress (thread_manager, (long) symbols [name]);

				return TargetAddress.Null;
			}
		}

		public Section this [long address] {
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

		public Section[] Sections {
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

				TargetAddress start = new TargetAddress (thread_manager, section.vma);
				TargetAddress end = start + section.size;

				TargetMemoryFlags flags = 0;
				if ((section.flags & SectionFlags.ReadOnly) != 0)
					flags |= TargetMemoryFlags.ReadOnly;

				if (list.Count > 0) {
					TargetMemoryArea last = (TargetMemoryArea) list [list.Count - 1];

					if ((last.Flags == flags) &&
					    ((last.End + 1 == start) || (last.End == start))) {
						list [list.Count - 1] = new TargetMemoryArea (
							last.Start, end, last.Flags, last.Name, inferior);
						continue;
					}
				}

				string name = section.bfd.FileName;
				list.Add (new TargetMemoryArea (start, end, flags, name, inferior));
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
			IntPtr section, data;
			int size;

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

		InternalSection GetSectionByName (string name)
		{
			IntPtr data = IntPtr.Zero;
			try {
				if (!bfd_glue_get_section_by_name (bfd, name, out data))
					throw new SymbolTableException (
						"Can't get bfd section {0}", name);

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

		public SourceInfo[] GetSources ()
		{
			if (dwarf == null)
				return null;

			return dwarf.GetSources ();
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

				return base_address;
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
					if (module != null)
						module.BfdDisposed ();
					if (dwarf != null)
						dwarf.Dispose ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					bfd_close (bfd);
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
