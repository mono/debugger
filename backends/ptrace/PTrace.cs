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

	internal class Inferior
	{
		IOChannel inferior_command;
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
		static extern bool mono_debugger_spawn_async (string working_directory, string[] argv, string[] envp, bool search_path, ChildSetupHandler child_setup, out int child_pid, out IntPtr status_channel, out IntPtr command_channel, ChildExitedHandler child_exited, ChildMessageHandler child_message, out int standard_input, out int standard_output, out int standard_error, out IntPtr errout);

		public Inferior (string working_directory, string[] argv, string[] envp)
		{
			this.working_directory = working_directory;
			this.argv = argv;
			this.envp = envp;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr status_channel, command_channel, error;

			string[] my_argv = { "mono-debugger-server",
					     OffsetTable.Magic.ToString ("x"),
					     OffsetTable.Version.ToString (),
					     "0", working_directory, "mono"
			};

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out command_channel,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			Console.WriteLine ("SPAWN: {0} {1}", retval, child_pid);

			if (!retval)
				throw new Exception ();

			inferior_command = new IOChannel (command_channel);
			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);
		}

		public Inferior (int pid, string[] envp)
		{
			this.envp = envp;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr status_channel, command_channel, error;

			string[] my_argv = { "mono-debugger-server",
					     OffsetTable.Magic.ToString ("x"),
					     OffsetTable.Version.ToString (),
					     pid.ToString ()
			};

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out command_channel,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			Console.WriteLine ("SPAWN: {0} {1}", retval, child_pid);

			if (!retval)
				throw new Exception ();

			inferior_command = new IOChannel (command_channel);
			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);
		}

		void child_exited ()
		{
			if (ChildExited != null)
				ChildExited ();
		}

		void child_message (ChildMessage message, int arg)
		{
			if (ChildMessage != null)
				ChildMessage (message, arg);
		}
	}

	public class PTrace : IDebuggerBackend, IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		ISourceFileFactory source_file_factory;

		Assembly application;
		Inferior inferior;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		public PTrace (string application, string[] arguments)
			: this (application, arguments, new SourceFileFactory ())
		{ }

		public PTrace (string application, string[] arguments, ISourceFileFactory source_factory)
		{
			NameValueCollection settings = ConfigurationSettings.AppSettings;

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "mono-path":
					Path_Mono = value;
					break;

				case "environment-path":
					Environment_Path = value;
					break;

				default:
					break;
				}
			}

			this.source_file_factory = source_factory;
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] argv = { Path_Mono, "--break", main_name, "--debug=mono",
					  "--noinline", "--nols", "--debug-args", "internal_mono_debugger",
					  application };
			string[] envp = { "PATH=" + Environment_Path };
			string working_directory = ".";

			Inferior inferior = new Inferior (working_directory, argv, envp);
			// Inferior inferior = new Inferior (31451, envp);
			inferior.ChildMessage += new ChildMessageHandler (child_message);
			inferior.ChildExited += new ChildExitedHandler (child_exited);
		}

		void child_message (ChildMessage message, int arg)
		{
			Console.WriteLine ("CHILD MESSAGE: {0} {1}", message, arg);
		}

		void child_exited ()
		{
			Console.WriteLine ("CHILD EXITED!");
		}

		//
		// IDebuggerBackend
		//

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				return target_state;
			}
		}

		public void Run ()
		{
		}

		public void Quit ()
		{
		}

		public void Abort ()
		{
		}

		public void Kill ()
		{
		}

		public void Frame ()
		{
		}

		public void Step ()
		{
		}
		
		public void Next ()
		{
		}

		public IBreakPoint AddBreakPoint (ITargetLocation location)
		{
			throw new NotImplementedException ();
		}

		TargetOutputHandler target_output = null;
		TargetOutputHandler target_error = null;
		StateChangedHandler state_changed = null;
		StackFrameHandler current_frame_event = null;
		StackFramesInvalidHandler frames_invalid_event = null;

		public event TargetOutputHandler TargetOutput {
			add {
				target_output += value;
			}

			remove {
				target_output -= value;
			}
		}

		public event TargetOutputHandler TargetError {
			add {
				target_error += value;
			}

			remove {
				target_error -= value;
			}
		}

		public event StateChangedHandler StateChanged {
			add {
				state_changed += value;
			}

			remove {
				state_changed -= value;
			}
		}

		public event StackFrameHandler CurrentFrameEvent {
			add {
				current_frame_event += value;
			}

			remove {
				current_frame_event -= value;
			}
		}

		public event StackFramesInvalidHandler FramesInvalidEvent {
			add {
				frames_invalid_event += value;
			}

			remove {
				frames_invalid_event -= value;
			}
		}

		public ISourceFileFactory SourceFileFactory {
			get {
				return source_file_factory;
			}

			set {
				source_file_factory = value;
			}
		}

		uint IDebuggerBackend.TargetAddressSize {
			get {
				return target_address_size;
			}
		}

		uint IDebuggerBackend.TargetIntegerSize {
			get {
				return target_integer_size;
			}
		}

		uint IDebuggerBackend.TargetLongIntegerSize {
			get {
				return target_long_integer_size;
			}
		}

		public byte ReadByte (long address)
		{
			throw new NotImplementedException ();
		}

		public uint ReadInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public int ReadSignedInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public long ReadAddress (long address)
		{
			throw new NotImplementedException ();
		}

		public long ReadLongInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public string ReadString (long address)
		{
			throw new NotImplementedException ();
		}

		public int ReadIntegerRegister (string name)
		{
			throw new NotImplementedException ();
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
					Quit ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					// Nothing to do yet.
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~PTrace ()
		{
			Dispose (false);
		}
	}
}
