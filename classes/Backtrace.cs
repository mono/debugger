using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class Backtrace : IDisposable
	{
		protected StackFrame[] frames;
		protected IProcess iprocess;
		protected IArchitecture arch;

		public Backtrace (IProcess iprocess, StackFrame[] frames)
		{
			this.iprocess = iprocess;
			this.frames = frames;

			arch = iprocess.Architecture;
		}

		public StackFrame[] Frames {
			get { return frames; }
		}

		public int Length {
			get {
				if (frames != null)
					return frames.Length;
				else
					return -1;
			}
		}

		public StackFrame this [int index] {
			get {
				if (frames == null)
					throw new ArgumentException ();
				else
					return frames [index];
			}
		}

		public event ObjectInvalidHandler BacktraceInvalidEvent;

		protected struct UnwindInfo {
			public readonly object Data;
			public readonly Register[] Registers;

			public UnwindInfo (object data, Register[] registers)
			{
				this.Data = data;
				this.Registers = registers;
			}
		}

		ArrayList unwind_info = null;

		protected UnwindInfo StartUnwindStack ()
		{
			if (unwind_info == null)
				unwind_info = new ArrayList ();
			if (unwind_info.Count > 0)
				return (UnwindInfo) unwind_info [0];

			Register[] registers = iprocess.GetRegisters ();
			object data = arch.UnwindStack (registers);

			UnwindInfo info = new UnwindInfo (data, registers);
			unwind_info.Add (info);
			return info;
		}

		protected UnwindInfo GetUnwindInfo (int level)
		{
			UnwindInfo start = StartUnwindStack ();
			if (level == 0)
				return start;
			else if (level < unwind_info.Count)
				return (UnwindInfo) unwind_info [level];

			level--;
			UnwindInfo last = GetUnwindInfo (level);
			StackFrame frame = frames [level];

			IMethod method = frame.Method;
			if ((method == null) || !method.IsLoaded || (last.Data == null)) {
				UnwindInfo new_info = new UnwindInfo (null, null);
				unwind_info.Add (new_info);
				return new_info;
			}

			int prologue_size;
			if (method.HasMethodBounds)
				prologue_size = (int) (method.MethodStartAddress - method.StartAddress);
			else
				prologue_size = (int) (method.EndAddress - method.StartAddress);
			int offset = (int) (frame.TargetAddress - method.StartAddress);
			prologue_size = Math.Min (prologue_size, offset);
			prologue_size = Math.Min (prologue_size, arch.MaxPrologueSize);

			byte[] prologue = iprocess.TargetMemoryAccess.ReadBuffer (
				method.StartAddress, prologue_size);

			object new_data;
			Register[] regs = arch.UnwindStack (
				prologue, iprocess.TargetMemoryAccess, last.Data, out new_data);

			UnwindInfo info = new UnwindInfo (new_data, regs);
			unwind_info.Add (info);
			return info;
		}

		public Register[] UnwindStack (int level)
		{
			UnwindInfo info = GetUnwindInfo (level);
			return info.Registers;
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Backtrace");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (BacktraceInvalidEvent != null)
						BacktraceInvalidEvent (this);
					if (frames != null) {
						foreach (StackFrame frame in frames)
							frame.Dispose ();
					}
				}
				
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Backtrace ()
		{
			Dispose (false);
		}

	}
}
