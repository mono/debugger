using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Architecture
{
	public class Bfd : IDisposable
	{
		IntPtr bfd;
		IInferior inferior;
		Hashtable symbols;
		DwarfReader dwarf;
		string filename;

		[DllImport("libbfd")]
		extern static void bfd_init ();

		[DllImport("libbfd")]
		extern static IntPtr bfd_openr (string filename, string target);

		[DllImport("libbfd")]
		extern static bool bfd_close (IntPtr bfd);

		[DllImport("libbfd")]
		extern static IntPtr bfd_get_section_by_name (IntPtr bfd, string name);

		[DllImport("libopcodes")]
		extern static IntPtr disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr bfd_glue_init_disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_check_format_object (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_get_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_get_section_contents (IntPtr bfd, IntPtr section, long offset, out IntPtr data, out int size);

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		[DllImport("libmonodebuggerbfdglue")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out long address);

		static Bfd ()
		{
			bfd_init ();
		}

		public Bfd (IInferior inferior, string filename, ISourceFileFactory factory)
		{
			bfd = bfd_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new TargetException ("Can't read symbol file: " + filename);

			if (!bfd_glue_check_format_object (bfd))
				throw new TargetException ("Not an object file: " + filename);

			this.inferior = inferior;
			this.filename = filename;

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

		public byte[] GetSectionContents (string name)
		{
			IntPtr section, data;
			int size;

			section = bfd_get_section_by_name (bfd, name);
			if (section == IntPtr.Zero)
				return null;

			if (!bfd_glue_get_section_contents (bfd, section, 0, out data, out size))
				return null;

			try {
				byte[] retval = new byte [size];
				Marshal.Copy (data, retval, 0, size);
				return retval;
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
