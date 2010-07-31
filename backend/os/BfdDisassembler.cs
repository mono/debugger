using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backend;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class BfdDisassembler : Disassembler, IDisposable
	{
		IntPtr handle;
		Process process;

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_disassemble_insn (IntPtr handle, long address);

		[DllImport("monodebuggerserver")]
		extern static IntPtr bfd_glue_create_disassembler (bool is_x86_64, ReadMemoryHandler read_memory_cb, OutputHandler output_cb, PrintAddressHandler print_address_cb);

		[DllImport("monodebuggerserver")]
		extern static void bfd_glue_free_disassembler (IntPtr handle);

		ReadMemoryHandler read_handler;
		OutputHandler output_handler;
		PrintAddressHandler print_handler;

		internal BfdDisassembler (Process process, bool is_x86_64)
		{
			this.process = process;

			read_handler = new ReadMemoryHandler (read_memory_func);
			output_handler = new OutputHandler (output_func);
			print_handler = new PrintAddressHandler (print_address_func);

			handle = bfd_glue_create_disassembler (
				is_x86_64, read_handler, output_handler, print_handler);
		}

		private delegate int ReadMemoryHandler (long address, IntPtr data, int size);
		private delegate void OutputHandler (string output);
		private delegate void PrintAddressHandler (long address);

		int read_memory_func (long address, IntPtr data, int size)
		{
			try {
				TargetAddress location = new TargetAddress (
					memory.AddressDomain, address);
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
		Method current_method;
		TargetMemoryAccess memory;

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
			TargetAddress maddress = new TargetAddress (
				memory.AddressDomain, address);

			if (current_method != null) {
				try {
					MethodSource method = current_method.GetTrampoline (
						memory, maddress);

					if (method != null) {
						output_func (method.Name);
						return;
					}
				} catch (TargetException) {
				}
			}

			Symbol name = null;
			if (process != null)
				name = process.SymbolTableManager.SimpleLookup (maddress, false);

			if (name == null)
				output_func (address);
			else
				output_func (String.Format ("0x{0:x}:{1}", address, name.ToString ()));
		}

		public override int GetInstructionSize (TargetMemoryAccess memory, TargetAddress address)
		{
			memory_exception = null;

			try {
				this.memory = memory;
				int count = bfd_glue_disassemble_insn (handle, address.Address);
				if (memory_exception != null)
					throw memory_exception;
				return count;
			} finally {
				this.memory = null;
				memory_exception = null;
			}
		}

		public override AssemblerMethod DisassembleMethod (TargetMemoryAccess memory, Method method)
		{
			lock (this) {
				ArrayList list = new ArrayList ();
				TargetAddress current = method.StartAddress;
				while (current < method.EndAddress) {
					AssemblerLine line = DisassembleInstruction (
						memory, method, current);
					if (line == null)
						break;

					current += line.InstructionSize;
					list.Add (line);
				}

				AssemblerLine[] lines = new AssemblerLine [list.Count];
				list.CopyTo (lines, 0);

				return new AssemblerMethod (method, lines);
			}
		}

		public override AssemblerLine DisassembleInstruction (TargetMemoryAccess memory,
								      Method method,
								      TargetAddress address)
		{
			lock (this) {
				memory_exception = null;
				sb = new StringBuilder ();

				address = new TargetAddress (memory.AddressDomain, address.Address);

				string insn;
				int insn_size;
				try {
					this.memory = memory;
					current_method = method;
					insn_size = bfd_glue_disassemble_insn (handle, address.Address);
					if (memory_exception != null)
						return null;
					insn = sb.ToString ();
				} finally {
					sb = null;
					this.memory = null;
					memory_exception = null;
					current_method = null;
				}

				Symbol label = null;
				if (process != null)
					label = process.SymbolTableManager.SimpleLookup (address, true);

				string label_name = null;
				if (label != null)
					label_name = label.ToString ();

				return new AssemblerLine (
					label_name, address, (byte) insn_size, insn);
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
					bfd_glue_free_disassembler (handle);
					handle = IntPtr.Zero;
				}
			}
		}

		public override void Dispose ()
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
