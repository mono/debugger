using GLib;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Backends
{
	internal enum ChildMessage {
		CHILD_EXITED = 1,
		CHILD_STOPPED,
		CHILD_SIGNALED
	}

	internal delegate void ChildSetupHandler ();
	internal delegate void ChildExitedHandler ();
	internal delegate void ChildMessageHandler (ChildMessage message, int arg);

	internal class Inferior : IDisposable
	{
		IntPtr server_handle;
		IOOutputChannel inferior_stdin;
		IOInputChannel inferior_stdout;
		IOInputChannel inferior_stderr;

		string working_directory;
		string[] argv;
		string[] envp;

		bool attached;

		int child_pid;

		public int PID {
			get {
				return child_pid;
			}
		}

		public event ChildExitedHandler ChildExited;
		public event ChildMessageHandler ChildMessage;

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_spawn_async (string working_directory, string[] argv, string[] envp, bool search_path, ChildSetupHandler child_setup, out int child_pid, out IntPtr status_channel, out IntPtr server_handle, ChildExitedHandler child_exited, ChildMessageHandler child_message, out int standard_input, out int standard_output, out int standard_error, out IntPtr errout);

		[DllImport("monodebuggerserver")]
		static extern ulong mono_debugger_get_program_counter (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern ulong mono_debugger_continue (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern ulong mono_debugger_detach (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern ulong mono_debugger_shutdown (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern ulong mono_debugger_kill (IntPtr handle);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_kill_process (int pid, bool force);

		public ulong GetProgramCounter ()
		{
			return mono_debugger_get_program_counter (server_handle);
		}

		public void Continue ()
		{
			mono_debugger_continue (server_handle);
		}

		public void Detach ()
		{
			mono_debugger_detach (server_handle);
		}

		public void Shutdown ()
		{
			mono_debugger_shutdown (server_handle);
		}

		public void Kill ()
		{
			mono_debugger_kill (server_handle);
		}

		public Inferior (string working_directory, string[] argv, string[] envp)
		{
			this.working_directory = working_directory;
			this.argv = argv;
			this.envp = envp;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr status_channel, error;

			string[] my_argv = { "mono-debugger-server",
					     OffsetTable.Magic.ToString ("x"),
					     OffsetTable.Version.ToString (),
					     "0", working_directory, "mono",
					     "./Foo.exe"
			};

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out server_handle,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			Console.WriteLine ("SPAWN: {0} {1}", retval, child_pid);

			if (!retval)
				throw new Exception ();

			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);
		}

		public Inferior (int pid, string[] envp)
		{
			this.envp = envp;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr status_channel, error;

			string[] my_argv = { "mono-debugger-server",
					     OffsetTable.Magic.ToString ("x"),
					     OffsetTable.Version.ToString (),
					     pid.ToString ()
			};

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out server_handle,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			Console.WriteLine ("SPAWN: {0} {1}", retval, child_pid);

			if (!retval)
				throw new Exception ();

			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);
		}

		void child_exited ()
		{
			child_pid = 0;
			if (ChildExited != null)
				ChildExited ();
		}

		void child_message (ChildMessage message, int arg)
		{
			if (ChildMessage != null)
				ChildMessage (message, arg);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					if (child_pid != 0) {
						mono_debugger_glue_kill_process (child_pid, false);
						child_pid = 0;
					}
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Inferior ()
		{
			Dispose (false);
		}
	}
}
