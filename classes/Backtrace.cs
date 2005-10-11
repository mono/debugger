using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class Backtrace : MarshalByRefObject, IDisposable
	{
		protected StackFrame last_frame;
		protected ITargetMemoryAccess target;
		protected Architecture arch;
		protected TargetAddress until;
		protected int max_frames;
		protected bool finished;

		ArrayList frames;

		public Backtrace (ITargetMemoryAccess target, Architecture arch,
				  StackFrame first_frame)
			: this (target, arch, first_frame, TargetAddress.Null, -1)
		{ }

		public Backtrace (ITargetMemoryAccess target, Architecture arch,
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

		public void GetBacktrace (TargetAccess target, Architecture arch,
					  ISymbolTable symtab, ISimpleSymbolTable simple)
		{
			while (TryUnwind (target, arch, symtab, simple)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		public void GetBacktrace (TargetAccess target, Architecture arch,
					  ISymbolTable symtab, ISimpleSymbolTable simple,
					  TargetAddress stack)
		{
			SimpleStackFrame new_frame = arch.UnwindStack (
				target.TargetMemoryAccess, stack, last_frame.FrameAddress);
			if (new_frame == null)
				return;

			StackFrame frame = StackFrame.CreateFrame (
				last_frame.Process, target, new_frame, symtab, simple);

			frames.Add (frame);
			last_frame = frame;

			GetBacktrace (target, arch, symtab, simple);
		}

		public bool TryUnwind (TargetAccess target, Architecture arch,
				       ISymbolTable symtab, ISimpleSymbolTable simple_symtab)
		{
			if (finished)
				return false;

			SimpleStackFrame new_frame = null;
			try {
				new_frame = UnwindStack (target.TargetMemoryAccess, arch);
			} catch (TargetException) {
			}

			if (new_frame == null) {
				finished = true;
				return false;
			}

			if (!until.IsNull && (new_frame.Address == until))
				return false;

			StackFrame frame = StackFrame.CreateFrame (
				last_frame.Process, target, new_frame, symtab, simple_symtab);

			frames.Add (frame);
			last_frame = frame;
			return true;
		}

		protected SimpleStackFrame UnwindStack (ITargetMemoryAccess memory,
							Architecture arch)
		{
			IMethod method = last_frame.Method;
			SimpleStackFrame new_frame = null;
			if (method != null) {
				try {
					new_frame = method.UnwindStack (
						last_frame.SimpleFrame, memory, arch);
				} catch (TargetException) {
				}

				if (new_frame != null)
					return new_frame;
			}

			foreach (Module module in last_frame.Process.Debugger.Modules) {
				try {
					new_frame = module.UnwindStack (last_frame.SimpleFrame, memory);
				} catch {
					continue;
				}
				if (new_frame != null)
					return new_frame;
			}

			return arch.UnwindStack (
				memory, last_frame.SimpleFrame, last_frame.Name, null);
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
