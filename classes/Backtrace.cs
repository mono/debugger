using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void BacktraceInvalidHandler ();

	public abstract class Backtrace : IDisposable
	{
		protected StackFrame[] frames;

		public Backtrace (StackFrame[] frames)
		{
			this.frames = frames;
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

		public event BacktraceInvalidHandler BacktraceInvalidEvent;

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
						BacktraceInvalidEvent ();
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
