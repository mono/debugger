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
	internal class BfdDisassembler : IDisassembler, IDisposable
	{
		IntPtr dis;
		IntPtr info;

		ITargetMemoryAccess memory;
		ISymbolTable symbol_table;

		[DllImport("libmonodebuggerbfdglue")]
		extern static int bfd_glue_disassemble_insn (IntPtr dis, IntPtr info, long address);

		[DllImport("libmonodebuggerbfdglue")]
		extern static void bfd_glue_setup_disassembler (IntPtr info, ReadMemoryHandler read_memory_cb, OutputHandler output_cb, PrintAddressHandler print_address_cb);

		[DllImport("libmonodebuggerbfdglue")]
		extern static void bfd_glue_free_disassembler (IntPtr info);

		internal BfdDisassembler (ITargetMemoryAccess memory, IntPtr dis, IntPtr info)
		{
			this.dis = dis;
			this.info = info;
			this.memory = memory;

			bfd_glue_setup_disassembler (info, new ReadMemoryHandler (read_memory_func),
						     new OutputHandler (output_func),
						     new PrintAddressHandler (print_address_func));
		}

		private delegate int ReadMemoryHandler (long address, IntPtr data, int size);
		private delegate void OutputHandler (string output);
		private delegate void PrintAddressHandler (long address);

		int read_memory_func (long address, IntPtr data, int size)
		{
			try {
				TargetAddress location = new TargetAddress (memory.AddressDomain, address);
				byte[] buffer = memory.ReadBuffer (location, size);
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
			if (sb != null)
				sb.Append (output);
		}

		void output_func (long address)
		{
			output_func (String.Format ("0x{0:x}", address));
		}

		void print_address_func (long address)
		{
			if (symbol_table == null) {
				output_func (address);
				return;
			}

			TargetAddress maddress = new TargetAddress (memory.GlobalAddressDomain, address);
			IMethod method = symbol_table.Lookup (maddress);
			if (method == null) {
				output_func (address);
				return;
			}

			long offset = method.StartAddress.Address - address;
			if (offset > 0)
				output_func (String.Format ("{0}+{1:x}", method.Name, offset));
			else if (offset == 0)
				output_func (method.Name);
			else
				output_func (address);
		}

		//
		// IDisassembler
		//

		public ISymbolTable SymbolTable {
			get {
				return symbol_table;
			}

			set {
				symbol_table = value;
			}
		}

		public string DisassembleInstruction (ref TargetAddress location)
		{
			memory_exception = null;
			sb = new StringBuilder ();

			string insn;
			try {
				int count = bfd_glue_disassemble_insn (dis, info, location.Address);
				if (memory_exception != null)
					throw memory_exception;
				insn = sb.ToString ();
				location += count;
			} finally {
				sb = null;
				memory_exception = null;
			}

			return insn;
		}

		public int GetInstructionSize (TargetAddress location)
		{
			memory_exception = null;

			try {
				int count = bfd_glue_disassemble_insn (dis, info, location.Address);
				if (memory_exception != null)
					throw memory_exception;
				return count;
			} finally {
				memory_exception = null;
			}
		}

		public IMethodSource DisassembleMethod (IMethod method)
		{
			IMethod native_method = new NativeMethod (this, method);
			return native_method.Source;
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
