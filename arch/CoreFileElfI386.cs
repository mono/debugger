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
		extern static bool bfd_glue_core_file_elfi386_get_registers (IntPtr data, int size, IntPtr regs);

		long[] registers;

		public CoreFileElfI386 (DebuggerBackend backend, string application, string core_file,
					BfdContainer bfd_container)
			: base (backend, application, core_file, bfd_container)
		{
			registers = get_registers ();
		}

		long[] get_registers ()
		{
			IntPtr data = IntPtr.Zero, regs = IntPtr.Zero;

			byte[] section = core_bfd.GetSectionContents (".reg", true);

			try {
				regs = Marshal.AllocHGlobal (68);
				data = Marshal.AllocHGlobal (section.Length);
				Marshal.Copy (section, 0, data, section.Length);
				if (!bfd_glue_core_file_elfi386_get_registers (data, section.Length, regs))
					return null;
				int[] registers = new int [17];
				Marshal.Copy (regs, registers, 0, 17);
				long[] retval = new long [17];
				for (int i = 0; i < 17; i++)
					retval [i] = (uint) registers [i];
				return retval;
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
				if (regs != IntPtr.Zero)
					Marshal.FreeHGlobal (regs);
			}
		}

		public override IInferiorStackFrame[] GetBacktrace (int max_frames, TargetAddress stop)
		{
			uint ebp = (uint) GetRegister ((int) I386Register.EBP);
			uint eip = (uint) GetRegister ((int) I386Register.EIP);

			ArrayList frames = new ArrayList ();

			long stop_addr = 0;
			if (!stop.IsNull)
				stop_addr = stop.Address;

			while (ebp != 0) {
				if (eip == stop_addr)
					break;

				frames.Add (new CoreFileStackFrame (this, eip, ebp, ebp));

				if ((max_frames >= 0) && (frames.Count >= max_frames))
					break;

				eip = (uint) ReadInteger (new TargetAddress (AddressDomain, ebp + 4));
				ebp = (uint) ReadInteger (new TargetAddress (AddressDomain, ebp));
			}

			IInferiorStackFrame[] retval = new IInferiorStackFrame [frames.Count];
			frames.CopyTo (retval, 0);
			return retval;
		}

		protected override TargetAddress GetCurrentFrame ()
		{
			if (registers != null)
				return new TargetAddress (AddressDomain, registers [(int) I386Register.EIP]);
			else
				return TargetAddress.Null;
		}

		public override Register[] GetRegisters ()
		{
			if (registers == null)
				return null;

			Register[] retval = new Register [registers.Length];
			for (int i = 0; i < registers.Length; i++)
				retval [i] = new Register (i, registers [i]);

			return retval;
		}
	}
}
