using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal class Bfd : IDisposable
	{
		IntPtr bfd;
		protected IInferior inferior;
		Hashtable symbols;
		Hashtable section_hash;
		DwarfReader dwarf;
		string filename;
		bool is_coredump;

		internal struct InternalSection
		{
			public readonly int index;
			public readonly long vma;
			public readonly long size;
			public readonly IntPtr section;
		}

		public class Section
		{
			public readonly Bfd bfd;
			public readonly long vma;
			public readonly long size;
			public readonly ObjectCache contents;

			internal Section (Bfd bfd, InternalSection section)
			{
				this.bfd = bfd;
				this.vma = section.vma;
				this.size = section.size;
				contents = new ObjectCache (
					new ObjectCacheFunc (get_section_contents), section,
					new TimeSpan (0,5,0));
			}

			object get_section_contents (object user_data)
			{
				InternalSection section = (InternalSection) user_data;

				byte[] data = bfd.GetSectionContents (section.section, true);
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
				return String.Format ("BfdSection ({0:x},{1:x})", vma, size);
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

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		[DllImport("libmonodebuggerbfdglue")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (IInferior inferior, string filename, bool core_file, SourceFileFactory factory)
		{
			this.inferior = inferior;
			this.filename = filename;

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

				symbols.Add (name, address);
			}

			g_free (symtab);

			try {
				dwarf = new DwarfReader (inferior, this, factory);
			} catch (Exception e) {
				Console.WriteLine ("Can't read dwarf file {0}: {1}", filename, e);
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

		public BfdDisassembler GetDisassembler (ITargetMemoryAccess memory)
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
					return new TargetAddress (inferior, (long) symbols [name]);

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

				throw new SymbolTableException (String.Format (
					"No section contains address {0:x}.", address));
			}
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

		bool has_sections = false;
		Section[] sections = null;

		void read_sections ()
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
