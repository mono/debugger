using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Remoting
{
	public class DebuggerStream : Stream, IDisposable
	{
		int fd;

		[DllImport("monodebuggerremoting")]
		static extern int mono_debugger_remoting_stream_read (int fd, IntPtr data, int size);

		[DllImport("monodebuggerremoting")]
		static extern int mono_debugger_remoting_stream_write (int fd, IntPtr data, int size);

		internal DebuggerStream (int fd)
		{
			this.fd = fd;
		}

		public override bool CanRead {
			get { return true; }
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override bool CanSeek {
			get { return false; }
		}

		public override long Length {
			get {
				throw new NotSupportedException ();
			}
		}

		public override long Position {
			get {
				throw new NotSupportedException ();
			}

			set {
				throw new NotSupportedException ();
			}
		}

		public override void SetLength (long value)
		{
			throw new NotSupportedException ();
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			throw new NotSupportedException ();
		}

		public override void Flush ()
		{
		}

		public override int Read ([In,Out] byte[] buffer, int offset, int count)
		{
			if (offset < 0)
				throw new ArgumentException ();
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (count);
				int ret = mono_debugger_remoting_stream_read (fd, data, count);
				if (ret < 0)
					return 0;
				Marshal.Copy (data, buffer, offset, count);
				return ret;
			} finally {
				Marshal.FreeHGlobal (data);
			}
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			int ret = -1;
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (count);
				Marshal.Copy (buffer, offset, data, count);
				ret = mono_debugger_remoting_stream_write (fd, data, count);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}

			if (ret != count)
				throw new DebuggerRemotingException ("Write failed");
		}
	}
}
