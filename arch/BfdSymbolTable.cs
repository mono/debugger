using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Architecture
{
	public class BfdSymbolTable : IDisposable
	{
		IntPtr bfd;
		Hashtable symbols;

		[DllImport("libbfd")]
		extern static void bfd_init ();

		[DllImport("libbfd")]
		extern static IntPtr bfd_openr (string filename, string target);

		[DllImport("libbfd")]
		extern static bool bfd_close (IntPtr bfd);

		[DllImport("libopcodes")]
		extern static IntPtr disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static IntPtr bfd_glue_init_disassembler (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_check_format_object (IntPtr bfd);

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_get_symbols (IntPtr bfd, out IntPtr symtab);

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		[DllImport("libmonodebuggerbfdglue")]
		extern static string bfd_glue_get_symbol (IntPtr bfd, IntPtr symtab, int index,
							  out long address);

		static BfdSymbolTable ()
		{
			bfd_init ();
		}

		public BfdSymbolTable (string filename)
		{
			bfd = bfd_openr (filename, null);
			if (bfd == IntPtr.Zero)
				throw new TargetException ("Can't read symbol file: " + filename);

			if (!bfd_glue_check_format_object (bfd))
				throw new TargetException ("Not an object file: " + filename);

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
		}

		public BfdDisassembler GetDisassembler (ITargetMemoryAccess memory)
		{
			IntPtr dis = disassembler (bfd);

			IntPtr info = bfd_glue_init_disassembler (bfd);

			return new BfdDisassembler (memory, dis, info);
		}

		public ITargetLocation this [string name] {
			get {
				if (symbols == null)
					return null;

				if (symbols.Contains (name))
					return new TargetLocation ((long) symbols [name]);

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
					// Do stuff here
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

		~BfdSymbolTable ()
		{
			Dispose (false);
		}
	}
}
