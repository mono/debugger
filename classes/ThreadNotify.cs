using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void ReadyEventHandler ();

	public class ThreadNotify : IDisposable
	{
		int input_fd, output_fd;
		IOInputChannel input_channel;
		IOOutputChannel output_channel;
		ReadyEventHandler ready_event;
		ArrayList listeners;

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unix_new (int fd);

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unref (IntPtr channel);

		public void Signal ()
		{
			Signal (0);
		}

		public void Signal (int id)
		{
			check_disposed ();
			output_channel.WriteInteger (id);
		}

		void read_data_handler (int data)
		{
			check_disposed ();
			if (data >= listeners.Count)
				return;

			ReadyEventHandler handler = (ReadyEventHandler) listeners [data];
			if (handler != null)
				handler ();
		}

		public ThreadNotify (ReadyEventHandler ready_event)
		{
			listeners = new ArrayList ();
			listeners.Add (ready_event);

			mono_debugger_glue_make_pipe (out input_fd, out output_fd);

			input_channel = new IOInputChannel (input_fd, false, true);
			output_channel = new IOOutputChannel (output_fd, false, true);

			input_channel.ReadDataEvent += new ReadDataHandler (read_data_handler);
		}

		public ThreadNotify ()
			: this (null)
		{ }

		public int RegisterListener (ReadyEventHandler ready_event)
		{
			int id = listeners.Count;
			listeners.Add (ready_event);
			return id;
		}

		public void UnRegisterListener (int id)
		{
			listeners [id] = null;
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
