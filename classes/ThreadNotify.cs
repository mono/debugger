using GLib;
using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class ThreadNotify : IDisposable
	{
		int input_fd, output_fd;
		IODataInputChannel input_channel;
		IODataOutputChannel output_channel;
		ReadyEventHandler ready_event;

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unix_new (int fd);

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unref (IntPtr channel);

		public void Signal ()
		{
			output_channel.WriteByte (0);
		}

		void ready_event_handler ()
		{
			input_channel.ReadByte ();
			ready_event ();
		}

		public ThreadNotify (ReadyEventHandler ready_event)
		{
			this.ready_event = ready_event;

			mono_debugger_glue_make_pipe (out input_fd, out output_fd);

			input_channel = new IODataInputChannel (
				input_fd, new ReadyEventHandler (ready_event_handler));
			output_channel = new IODataOutputChannel (output_fd);
		}

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_make_pipe (out int input_fd, out int output_fd);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_close_pipe (int input_fd, int output_fd);

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadNotify");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					input_channel.Dispose ();
					output_channel.Dispose ();
				}

				this.disposed = true;

				lock (this) {
					mono_debugger_glue_close_pipe (input_fd, output_fd);
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ThreadNotify ()
		{
			Dispose (false);
		}

	}

}
