using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void StackFrameHandler (StackFrame frame);
	public delegate void StackFrameInvalidHandler ();

	public sealed class StackFrame : IDisposable
	{
		IMethod method;
		TargetAddress address;
		ITargetMemoryAccess memory;
		SourceLocation source;
		object handle;
		int level;

		public StackFrame (ITargetMemoryAccess memory, TargetAddress address, object handle,
				   int level, SourceLocation source, IMethod method)
			: this (memory, address, handle, level)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (ITargetMemoryAccess memory, TargetAddress address, object handle,
				   int level)
		{
			this.memory = memory;
			this.address = address;
			this.handle = handle;
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

		public IMethod Method {
			get {
				check_disposed ();
				return method;
			}
		}

		public object Handle {
			get {
				check_disposed ();
				return handle;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_disposed ();
				return memory;
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
					memory = null;
					source = null;
					handle = null;
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
