using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal class CoreFileElfI386 : CoreFile
	{
		[DllImport("libmonodebuggerbfdglue")]
		extern static bool bfd_glue_core_file_elfi386_get_registers (IntPtr data, int size, out IntPtr regs);

		long[] registers;

		public CoreFileElfI386 (string application, string core_file, BfdContainer bfd_container)
			: base (application, core_file, bfd_container)
		{
			registers = get_registers ();
		}

		long[] get_registers ()
		{
			IntPtr data = IntPtr.Zero;

			byte[] notes = core_bfd.GetSectionContents ("note0", true);

			try {
				IntPtr regs;
				data = Marshal.AllocHGlobal (notes.Length);
				Marshal.Copy (notes, 0, data, notes.Length);
				if (!bfd_glue_core_file_elfi386_get_registers (data, notes.Length, out regs))
					throw new InvalidCoreFileException ("Can't get registers");
				int[] registers = new int [18];
				Marshal.Copy (regs, registers, 0, 18);
				long[] retval = new long [18];
				for (int i = 0; i < 18; i++)
					retval [i] = registers [i];
				return retval;
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public override IInferiorStackFrame[] GetBacktrace (int max_frames, bool full_backtrace)
		{
			uint ebp = (uint) GetRegister ((int) I386Register.EBP);
			uint eip = (uint) GetRegister ((int) I386Register.EIP);

			ArrayList frames = new ArrayList ();

			while (ebp != 0) {
				frames.Add (new CoreFileStackFrame (this, eip, ebp, ebp));

				if ((max_frames >= 0) && (frames.Count >= max_frames))
					break;

				eip = (uint) ReadInteger (new TargetAddress (this, ebp + 4));
				ebp = (uint) ReadInteger (new TargetAddress (this, ebp));
			}

			IInferiorStackFrame[] retval = new IInferiorStackFrame [frames.Count];
			frames.CopyTo (retval, 0);
			return retval;
		}

		public override TargetAddress CurrentFrame {
			get {
				return new TargetAddress (this, registers [(int) I386Register.EIP]);
			}
		}

		public override long GetRegister (int index)
		{
			return registers [index];
		}

		public override long[] GetRegisters (int[] indices)
		{
			long[] retval = new long [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				retval [i] = registers [indices [i]];

			return retval;
		}
	}
}
