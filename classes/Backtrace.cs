using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class Backtrace : IDisposable
	{
		protected StackFrame last_frame;
		protected ITargetMemoryAccess target;
		protected IArchitecture arch;
		protected TargetAddress until;
		protected int max_frames;
		protected bool finished;

		ArrayList frames;

		public Backtrace (ITargetMemoryAccess target, IArchitecture arch,
				  StackFrame first_frame)
			: this (target, arch, first_frame, TargetAddress.Null, -1)
		{ }

		public Backtrace (ITargetMemoryAccess target, IArchitecture arch,
				  StackFrame first_frame, TargetAddress until,
				  int max_frames)
		{
			this.target = target;
			this.arch = arch;
			this.last_frame = first_frame;
			this.until = until;
			this.max_frames = max_frames;

			frames = new ArrayList ();
			frames.Add (first_frame);
		}

		public StackFrame[] Frames {
			get {
				StackFrame[] retval = new StackFrame [frames.Count];
				frames.CopyTo (retval, 0);
				return retval;
			}
		}

		public event ObjectInvalidHandler BacktraceInvalidEvent;

		public void GetBacktrace (ITargetAccess target, IArchitecture arch,
					  ISymbolTable symtab, ISimpleSymbolTable simple)
		{
			while (TryUnwind (target, arch, symtab, simple)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		public bool TryUnwind (ITargetAccess target, IArchitecture arch,
				       ISymbolTable symtab, ISimpleSymbolTable simple_symtab)
		{
			if (finished)
				return false;

			SimpleStackFrame new_frame = UnwindStack (target, arch);
			if (new_frame == null) {
				finished = true;
				return false;
			}

			if (!until.IsNull && (new_frame.Address == until))
				return false;

			StackFrame frame = StackFrame.CreateFrame (
				last_frame.Process, new_frame, symtab, simple_symtab);

			frames.Add (frame);
			last_frame = frame;
			return true;
		}

		protected SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
							IArchitecture arch)
		{
			IMethod method = last_frame.Method;
			if (method != null)
				return method.UnwindStack (
					last_frame.SimpleFrame, memory, arch);

			foreach (Module module in last_frame.Process.DebuggerBackend.Modules) {
				SimpleStackFrame new_frame = module.UnwindStack (
					last_frame.SimpleFrame, memory);
				if (new_frame != null)
					return new_frame;
			}

			return arch.UnwindStack (last_frame.SimpleFrame, null, memory);
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
