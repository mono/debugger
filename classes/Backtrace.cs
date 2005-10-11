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

		public void GetBacktrace (TargetAccess target, Architecture arch)
		{
			while (TryUnwind (target, arch)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		public void GetBacktrace (TargetAccess target, Architecture arch,
					  TargetAddress stack)
		{
			StackFrame new_frame = arch.UnwindStack (last_frame, target.TargetMemoryAccess);
			if (new_frame == null)
				return;

			frames.Add (new_frame);
			last_frame = new_frame;

			GetBacktrace (target, arch);
		}

		public bool TryUnwind (TargetAccess target, Architecture arch)
		{
			if (finished)
				return false;

			StackFrame new_frame = null;
			try {
				new_frame = UnwindStack (target.TargetMemoryAccess, arch);
			} catch (TargetException) {
			}

			if (new_frame == null) {
				finished = true;
				return false;
			}

			if (!until.IsNull && (new_frame.TargetAddress == until))
				return false;

			frames.Add (new_frame);
			last_frame = new_frame;
			return true;
		}

		StackFrame UnwindStack (ITargetMemoryAccess memory, Architecture arch)
		{
			Method method = last_frame.Method;
			StackFrame new_frame = null;
			if (method != null) {
				try {
					new_frame = method.UnwindStack (last_frame, memory, arch);
				} catch (TargetException) {
				}

				if (new_frame != null)
					return new_frame;
			}

			foreach (Module module in last_frame.Process.Debugger.Modules) {
				try {
					new_frame = module.UnwindStack (last_frame, memory);
				} catch {
					continue;
				}
				if (new_frame != null)
					return new_frame;
			}

			return arch.UnwindStack (last_frame, memory);
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
