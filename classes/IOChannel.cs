// GLib.IOChannel.cs - GIOChannel class implementation
//
// Author: Martin Baulig <martin@gnome.org>
//
// (c) 2002 Ximian, Inc

namespace GLib {

	using System;
	using System.Text;
	using System.Runtime.InteropServices;

	public class IOChannel
	{
		protected IntPtr _channel;

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unix_new (int fd);

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_glue_add_watch_hangup (IntPtr channel, HangupHandler cb);

		public event HangupHandler Hangup;

		void hangup ()
		{
			if (Hangup != null)
				Hangup ();
		}

		internal IOChannel (IntPtr _channel)
		{
			this._channel = _channel;
			mono_debugger_glue_add_watch_hangup (_channel, new HangupHandler (hangup));
		}

		internal IOChannel (int fd)
		{
			_channel = g_io_channel_unix_new (fd);
			mono_debugger_glue_add_watch_hangup (_channel, new HangupHandler (hangup));
		}
	}

	public delegate void HangupHandler ();
	public delegate void ReadLineHandler (string line);

	public class IOInputChannel : IOChannel
	{
		public event ReadLineHandler ReadLine;

		//		
		// Everything below is private.
		//

		public IOInputChannel (int fd)
			: base (fd)
		{
			mono_debugger_glue_add_watch_input (_channel, new ReadLineHandler (read_line));
		}

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_glue_add_watch_input (IntPtr channel, ReadLineHandler cb);

		StringBuilder sb = null;

		void read_line (string line)
		{
			if (ReadLine == null)
				return;

			int start = 0;
			int length = line.Length;
			while (true) {
				int end = line.IndexOf ('\n', start);
				if (end != -1) {
					if (start != end) {
						if (sb != null) {
							sb.Append (line.Substring (start, end-start));
							ReadLine (sb.ToString ());
							sb = null;
						} else
							ReadLine (line.Substring (start, end-start));
					} else if (sb != null) {
						ReadLine (sb.ToString ());
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
	}

	public class IOOutputChannel : IOChannel
	{
		public void WriteLine (string line)
		{
			mono_debugger_glue_write_line (_channel, line + '\n');
		}

		//		
		// Everything below is private.
		//

		public IOOutputChannel (int fd)
			: base (fd)
		{
			mono_debugger_glue_add_watch_output (_channel);
		}

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_add_watch_output (IntPtr channel);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_write_line (IntPtr channel, string line);
	}

	public class Spawn
	{
		int child_pid;
		int stdin_fd, stdout_fd, stderr_fd;

		[DllImport("glib-2.0")]
		static extern bool g_spawn_async_with_pipes (string working_directory, string[] argv, string[] envp, int flags, IntPtr child_setup, IntPtr user_data, out int child_pid, out int standard_input, out int standard_output, out int standard_error, out IntPtr errout);

		[DllImport("glib-2.0")]
		static extern bool g_spawn_command_line_sync (string command_line, out IntPtr standard_output, out IntPtr standard_error, out int exit_status, out IntPtr errout);

		[DllImport("glib-2.0")]
		static extern void g_free (IntPtr memory);	

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_kill_process (int pid, bool force);

		public Spawn (string working_directory, string[] argv, string[] envp,
			      out IOOutputChannel standard_input, out IOInputChannel standard_output,
			      out IOInputChannel standard_error)
		{
			IntPtr error;

			bool retval = g_spawn_async_with_pipes (working_directory, argv, envp, 0,
								IntPtr.Zero, IntPtr.Zero,
								out child_pid, out stdin_fd,
								out stdout_fd, out stderr_fd,
								out error);
			if (!retval)
				throw new Exception ();

			standard_input = new IOOutputChannel (stdin_fd);
			standard_output = new IOInputChannel (stdout_fd);
			standard_error = new IOInputChannel (stderr_fd);
		}

		public static int SpawnCommandLine (string command_line, out string standard_output,
						    out string standard_error)
		{
			int exit_status;
			IntPtr str_stdout, str_stderr, error;
			bool retval = g_spawn_command_line_sync (
				command_line, out str_stdout, out str_stderr, out exit_status, out error);

			if (!retval)
				throw new Exception ();

			standard_output = Marshal.PtrToStringAnsi (str_stdout);
			standard_error = Marshal.PtrToStringAnsi (str_stderr);

			g_free (str_stdout);
			g_free (str_stderr);

			return exit_status;
		}

		public void Kill ()
		{
			if (child_pid != -1) {
				mono_debugger_glue_kill_process (child_pid, true);
				child_pid = -1;
			}
		}
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
