// GLib.IOChannel.cs - GIOChannel class implementation
//
// Author: Martin Baulig <martin@gnome.org>
//
// (c) 2002 Ximian, Inc

namespace Mono.Debugger {

	using System;
	using System.Text;
	using System.Runtime.InteropServices;

	public class IOChannel : IDisposable
	{
		protected IntPtr _channel;
		protected bool is_async;
		protected bool is_data;
		uint hangup_id;

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unix_new (int fd);

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unref (IntPtr channel);

		[DllImport("glib-2.0")]
		protected static extern bool g_source_remove (uint tag);

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_io_add_watch_hangup (IntPtr channel, HangupHandler cb);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_io_set_async (IntPtr channel, bool is_async);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_io_set_data_mode (IntPtr channel);

		public event HangupHandler Hangup;

		void hangup ()
		{
			if (Hangup != null)
				Hangup ();
		}

		internal IOChannel (IntPtr _channel)
		{
			this._channel = _channel;
			hangup_id = mono_debugger_io_add_watch_hangup (_channel, new HangupHandler (hangup));
		}

		public IOChannel (int fd, bool is_async, bool is_data)
		{
			this.is_async = is_async;
			this.is_data = is_data;

			_channel = g_io_channel_unix_new (fd);
			hangup_id = mono_debugger_io_add_watch_hangup (_channel, new HangupHandler (hangup));
			mono_debugger_io_set_async (_channel, is_async);
			if (is_data)
				mono_debugger_io_set_data_mode (_channel);
		}

		public IntPtr Channel {
			get { return _channel; }
		}

		public bool IsAsync {
			get { return is_async; }

			set {
				if (value == is_async)
					return;

				is_async = value;
				mono_debugger_io_set_async (_channel, is_async);
			}
		}

		public bool IsData {
			get { return is_data; }
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				this.disposed = true;

				lock (this) {
					g_source_remove (hangup_id);
					g_io_channel_unref (_channel);
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~IOChannel ()
		{
			Dispose (false);
		}
	}

	public delegate void HangupHandler ();
	public delegate void ReadLineHandler (string line);
	public delegate void ReadDataHandler (int data);

	public class IOInputChannel : IOChannel
	{
		public event ReadLineHandler ReadLineEvent;
		public event ReadDataHandler ReadDataEvent;

		public IOInputChannel (int fd, bool is_async, bool is_data)
			: base (fd, is_async, is_data)
		{
			if (is_data)
				watch_id = mono_debugger_io_add_watch_data_input (_channel, new ReadDataHandler (read_data));
			else
				watch_id = mono_debugger_io_add_watch_string_input (_channel, new ReadLineHandler (read_line));
		}

		//
		// Everything below is private.
		//

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_io_add_watch_data_input (IntPtr channel, ReadDataHandler cb);

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_io_add_watch_string_input (IntPtr channel, ReadLineHandler cb);

		uint watch_id;
		StringBuilder sb = null;

		void read_line (string line)
		{
			if (ReadLineEvent == null)
				return;

			int start = 0;
			int length = line.Length;
			while (true) {
				int end = line.IndexOf ('\n', start);
				if (end != -1) {
					if (start != end) {
						if (sb != null) {
							sb.Append (line.Substring (start, end-start));
							ReadLineEvent (sb.ToString ());
							sb = null;
						} else
							ReadLineEvent (line.Substring (start, end-start));
					} else if (sb != null) {
						ReadLineEvent (sb.ToString ());
						sb = null;
					}
					start = end + 1;
				} else {
					if (start != length) {
						if (sb != null)
							sb.Append (line.Substring (start));
						else
							sb = new StringBuilder (line.Substring (start));
					}
					break;
				}
			}
		}

		void read_data (int data)
		{
			if (ReadDataEvent != null)
				ReadDataEvent (data);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected override void Dispose (bool disposing)
		{
			if (!this.disposed) {
				this.disposed = true;

				lock (this) {
					g_source_remove (watch_id);
				}
			}

			base.Dispose (disposing);
		}
	}

	public class IOOutputChannel : IOChannel
	{
		public void WriteLine (string line)
		{
			mono_debugger_io_write_line (_channel, line + '\n');
		}

		public void WriteByte (int data)
		{
			mono_debugger_io_write_byte (_channel, data);
		}

		public void WriteInteger (int data)
		{
			mono_debugger_io_write_integer (_channel, data);
		}

		public IOOutputChannel (int fd, bool is_async, bool is_data)
			: base (fd, is_async, is_data)
		{ }

		//
		// Everything below is private.
		//

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_io_write_line (IntPtr channel, string line);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_io_write_byte (IntPtr channel, int data);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_io_write_integer (IntPtr channel, int data);
	}

	public delegate bool GSourceFunc ();

	public class IdleHandler : IDisposable
	{
		uint tag;

		public IdleHandler (GSourceFunc source_func)
		{
			tag = g_idle_add (source_func, IntPtr.Zero);
		}

		[DllImport("glib-2.0")]
		static extern uint g_idle_add (GSourceFunc func, IntPtr data);

		[DllImport("glib-2.0")]
		static extern void g_source_remove (uint tag);

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			if (!disposed) {
				if (disposing) {
					g_source_remove (tag);
				}
				
				disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~IdleHandler ()
		{
			Dispose (false);
		}
	}
}
