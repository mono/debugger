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

		public StackFrame (ITargetMemoryAccess memory, TargetAddress address, object handle,
				   SourceLocation source, IMethod method)
			: this (memory, address, handle)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (ITargetMemoryAccess memory, TargetAddress address, object handle)
		{
			this.memory = memory;
			this.address = address;
			this.handle = handle;
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
			StringBuilder builder = new StringBuilder ();

			if (source != null) {
				builder.Append (source);
				builder.Append (" at ");
			}
			builder.Append (address);

			return builder.ToString ();
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
