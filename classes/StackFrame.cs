using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void StackFrameHandler (StackFrame frame);
	public delegate void StackFrameInvalidHandler ();

	public abstract class StackFrame : IDisposable
	{
		IMethod method;
		TargetAddress address;
		SourceLocation source;
		int level;

		public StackFrame (TargetAddress address, int level,
				   SourceLocation source, IMethod method)
			: this (address, level)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (TargetAddress address, int level)
		{
			this.address = address;
			this.level = level;
		}

		public int Level {
			get {
				return level;
			}
		}

		public bool IsValid {
			get {
				return !disposed;
			}
		}

		public SourceLocation SourceLocation {
			get {
				check_disposed ();
				return source;
			}
		}

		public TargetAddress TargetAddress {
			get {
				check_disposed ();
				return address;
			}
		}

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract TargetAddress LocalsAddress {
			get;
		}

		public abstract TargetAddress ParamsAddress {
			get;
		}

		public IMethod Method {
			get {
				check_disposed ();
				return method;
			}
		}

		public event StackFrameInvalidHandler FrameInvalid;

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append (String.Format ("#{0}: ", level));

			if (method != null) {
				sb.Append (String.Format ("{0} in {1}", address, method.Name));
				if (method.IsLoaded) {
					long offset = address - method.StartAddress;
					if (offset > 0)
						sb.Append (String.Format ("+0x{0:x}", offset));
					else if (offset < 0)
						sb.Append (String.Format ("-0x{0:x}", -offset));
				}
			} else
				sb.Append (String.Format ("{0}", address));

			if (source != null)
				sb.Append (String.Format (" at {0}", source.Name));

			return sb.ToString ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("StackFrame");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (FrameInvalid != null)
						FrameInvalid ();

					method = null;
					source = null;
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

		~StackFrame ()
		{
			Dispose (false);
		}

	}
}
