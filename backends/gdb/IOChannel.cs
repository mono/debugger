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
		IntPtr _channel;

		[DllImport("glib-2.0")]
		static extern IntPtr g_io_channel_unix_new (int fd);

		internal IOChannel (int fd)
		{
			_channel = g_io_channel_unix_new (fd);
		}
	}

	public delegate void ReadLineHandler (string line);

	public class IOInputChannel : IOChannel
	{
		public event ReadLineHandler ReadLine;

		//		
		// Everything below is private.
		//

		internal IOInputChannel (int fd)
			: base (fd)
		{
			mono_debugger_glue_add_watch_input (_channel, new ReadLineHandler (read_line));
		}

		[DllImport("monodebuggerglue")]
		static extern uint mono_debugger_glue_add_watch_input (IntPtr channel, ReadLineHandler cb);

		void read_line (string line)
		{
			if (ReadLine == null)
				return;

			int start = 0;
			int length = line.Length;
			while (true) {
				int end = line.IndexOf ('\n', start);
				if (end != -1) {
					if (start != end)
						ReadLine (line.Substring (start, end-start));
					start = end + 1;
				} else {
					if (start != length)
						ReadLine (line.Substring (start));
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

		internal IOOutputChannel (int fd)
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

		public void Kill ()
		{
			if (child_pid != -1) {
				mono_debugger_glue_kill_process (child_pid, true);
				child_pid = -1;
			}
		}
	}
}

