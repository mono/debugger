using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class StackFrame : IDisposable
	{
		IMethod method;
		TargetAddress address;
		DebuggerBackend backend;
		ITargetMemoryAccess memory;
		ISourceLocation source;
		object frame_handle;

		public StackFrame (DebuggerBackend backend, ITargetMemoryAccess memory,
				   TargetAddress address, object frame_handle,
				   ISourceLocation source, IMethod method)
			: this (backend, memory, address, frame_handle)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (DebuggerBackend backend, ITargetMemoryAccess memory,
				   TargetAddress address, object frame_handle)
		{
			this.backend = backend;
			this.memory = memory;
			this.address = address;
			this.frame_handle = frame_handle;
		}

		public bool IsValid {
			get {
				return !disposed;
			}
		}

		public ISourceLocation SourceLocation {
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

		public object FrameHandle {
			get {
				check_disposed ();
				return frame_handle;
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
					frame_handle = null;
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
