using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void AsyncQueueHandler (string message);

	public class AsyncQueue : IDisposable
	{
		int input_fd, output_fd;
		IOInputChannel input_channel;
		IOOutputChannel output_channel;
		AsyncQueueHandler handler;

		public AsyncQueue (AsyncQueueHandler handler)
		{
			this.handler = handler;

			mono_debugger_glue_make_pipe (out input_fd, out output_fd);
			input_channel = new IOInputChannel (input_fd, true, false);
			output_channel = new IOOutputChannel (output_fd, false, false);

			input_channel.ReadLineEvent += new ReadLineHandler (read_line_handler);
		}

		public void Write (string message)
		{
			check_disposed ();
			output_channel.WriteLine (message);
		}

		public void Write ()
		{
			Write (" ");
		}

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_make_pipe (out int input_fd, out int output_fd);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_close_pipe (int input_fd, int output_fd);

		void read_line_handler (string line)
		{
			handler (line);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("AsyncQueue");
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

		~AsyncQueue ()
		{
			Dispose (false);
		}

	}

}
