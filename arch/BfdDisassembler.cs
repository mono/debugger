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
		ISimpleSymbolTable symbol_table;

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
		IMethod current_method;
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

#if FIXME
			if (current_method != null) {
				SourceMethod method = current_method.GetTrampoline (maddress);

				if (method != null) {
					output_func (method.Name);
					return;
				}
			}
#endif

			string name = symbol_table.SimpleLookup (maddress, false);

			if (name == null)
				output_func (address);
			else
				output_func (name);
		}

		//
		// IDisassembler
		//

		public ISimpleSymbolTable SymbolTable {
			get {
				return symbol_table;
			}

			set {
				symbol_table = value;
			}
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

		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			lock (this) {
				ArrayList list = new ArrayList ();
				TargetAddress current = method.StartAddress;
				while (current < method.EndAddress) {
					AssemblerLine line = DisassembleInstruction (method, current);
					if (line == null)
						break;

					current += line.InstructionSize;
					list.Add (line);
				}

				AssemblerLine[] lines = new AssemblerLine [list.Count];
				list.CopyTo (lines, 0);

				return new AssemblerMethod (
					method.StartAddress, method.EndAddress, method.Name, lines);
			}
		}

		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			lock (this) {
				memory_exception = null;
				sb = new StringBuilder ();

				string insn;
				int insn_size;
				try {
					current_method = method;
					insn_size = bfd_glue_disassemble_insn (dis, info, address.Address);
					if (memory_exception != null)
						return null;
					insn = sb.ToString ();
				} finally {
					sb = null;
					memory_exception = null;
					current_method = null;
				}

				string label = null;
				if (SymbolTable != null)
					label = SymbolTable.SimpleLookup (address, true);

				return new AssemblerLine (label, address, (byte) insn_size, insn);
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
