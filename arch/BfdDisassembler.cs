using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Architecture
{
	public class BfdDisassembler : IDisassembler, IDisposable
	{
		IntPtr dis;
		IntPtr info;

		ITargetMemoryAccess memory;

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_disassemble_insn (IntPtr dis, IntPtr info, long address);

		[DllImport("libmonodebuggerbfdglue")]
		extern static void bfd_glue_setup_disassembler (IntPtr info, ReadMemoryHandler read_memory_cb, OutputHandler output_cb);

		[DllImport("libmonodebuggerbfdglue")]
		extern static void bfd_glue_free_disassembler (IntPtr info);

		internal BfdDisassembler (ITargetMemoryAccess memory, IntPtr dis, IntPtr info)
		{
			this.dis = dis;
			this.info = info;
			this.memory = memory;

			bfd_glue_setup_disassembler (info, new ReadMemoryHandler (read_memory_func),
						     new OutputHandler (output_func));
		}

		private delegate int ReadMemoryHandler (long address, IntPtr data, int size);
		private delegate void OutputHandler (string output);

		int read_memory_func (long address, IntPtr data, int size)
		{
			try {
				ITargetLocation location = new TargetLocation (address);
				byte[] buffer = memory.ReadBuffer (location, 0, size);
				Marshal.Copy (buffer, 0, data, size);
			} catch (Exception e) {
				memory_exception = e;
				return 1;
			}
			return 0;
		}

		StringBuilder sb;
		Exception memory_exception;
		void output_func (string output)
		{
			sb.Append (output);
		}

		//
		// IDisassembler
		//

		public string DisassembleInstruction (ref ITargetLocation location)
		{
			memory_exception = null;
			sb = new StringBuilder ();

			string insn;
			try {
				int count = bfd_glue_disassemble_insn (
					dis, info, location.Location + location.Offset);
				if (memory_exception != null)
					throw memory_exception;
				insn = sb.ToString ();
				location.Offset += count;
			} finally {
				sb = null;
				memory_exception = null;
			}

			return insn;
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
					bfd_glue_free_disassembler (info);
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BfdDisassembler ()
		{
			Dispose (false);
		}
	}
}
