using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
{
	internal class BfdDisassembler : Disassembler, IDisposable
	{
		IntPtr dis;
		IntPtr info;

		ITargetMemoryAccess memory;
		SymbolTableManager symtab_manager;

		[DllImport("monodebuggerserver")]
		extern static int bfd_glue_disassemble_insn (IntPtr dis, IntPtr info, long address);

		[DllImport("monodebuggerserver")]
		extern static void bfd_glue_setup_disassembler (IntPtr info, ReadMemoryHandler read_memory_cb, OutputHandler output_cb, PrintAddressHandler print_address_cb);

		[DllImport("monodebuggerserver")]
		extern static void bfd_glue_free_disassembler (IntPtr info);

		ReadMemoryHandler read_handler;
		OutputHandler output_handler;
		PrintAddressHandler print_handler;

		internal BfdDisassembler (SymbolTableManager symtab_manager,
					  ITargetMemoryAccess memory, IntPtr dis, IntPtr info)
		{
			this.dis = dis;
			this.info = info;
			this.memory = memory;
			this.symtab_manager = symtab_manager;

			read_handler = new ReadMemoryHandler (read_memory_func);
			output_handler = new OutputHandler (output_func);
			print_handler = new PrintAddressHandler (print_address_func);


			bfd_glue_setup_disassembler (info, read_handler, output_handler,
						     print_handler);
		}

		private delegate int ReadMemoryHandler (long address, IntPtr data, int size);
		private delegate void OutputHandler (string output);
		private delegate void PrintAddressHandler (long address);

		int read_memory_func (long address, IntPtr data, int size)
		{
			try {
				TargetAddress location = new TargetAddress (
					memory.TargetInfo.AddressDomain, address);
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
				memory.TargetInfo.AddressDomain, address);

			if (current_method != null) {
				try {
					SourceMethod method = current_method.GetTrampoline (
						memory, maddress);

					if (method != null) {
						output_func (method.Name);
						return;
					}
				} catch (TargetException) {
				}
			}

			Symbol name = symtab_manager.SimpleLookup (maddress, false);

			if (name == null)
				output_func (address);
			else
				output_func (String.Format ("0x{0:x}:{1}", address, name.ToString ()));
		}

		public override int GetInstructionSize (TargetAddress location)
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

		public override AssemblerMethod DisassembleMethod (Method method)
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
					method.Module, method.StartAddress, method.EndAddress,
					method.Name, lines);
			}
		}

		public override AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address)
		{
			lock (this) {
				memory_exception = null;
				sb = new StringBuilder ();

				address = new TargetAddress (
					memory.TargetInfo.AddressDomain, address.Address);

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

				Symbol label = null;
				label = symtab_manager.SimpleLookup (address, true);

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
					bfd_glue_free_disassembler (info);
					info = IntPtr.Zero;
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
