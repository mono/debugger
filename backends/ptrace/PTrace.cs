using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
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
using Mono.Debugger.Architecture;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Backends
{
	internal enum ChildMessageType {
		CHILD_EXITED = 1,
		CHILD_STOPPED,
		CHILD_SIGNALED,
		CHILD_CALLBACK
	}

	internal enum CommandError {
		NONE = 0,
		IO,
		UNKNOWN,
		INVALID_COMMAND,
		NOT_STOPPED
	}
	
	internal enum ServerCommand {
		GET_PC = 1,
		DETACH,
		SHUTDOWN,
		KILL,
		CONTINUE,
		STEP
	}

	internal delegate void ChildSetupHandler ();
	internal delegate void ChildExitedHandler ();
	internal delegate void ChildCallbackHandler (long argument, long data);
	internal delegate void ChildMessageHandler (ChildMessageType message, int arg);

	internal class Inferior : IInferior, IDisposable
	{
		IntPtr server_handle;
		IOOutputChannel inferior_stdin;
		IOInputChannel inferior_stdout;
		IOInputChannel inferior_stderr;

		string working_directory;
		string[] argv;
		string[] envp;

		BfdSymbolTable bfd_symtab;
		BfdDisassembler bfd_disassembler;

		bool attached;

		int child_pid;

		ITargetInfo target_info;
		Hashtable pending_callbacks = new Hashtable ();
		long last_callback_id = 0;

		public int PID {
			get {
				return child_pid;
			}
		}

		MonoDebuggerInfo mono_debugger_info = null;
		public MonoDebuggerInfo MonoDebuggerInfo {
			get {
				return mono_debugger_info;
			}
		}

		public event ChildExitedHandler ChildExited;
		public event ChildMessageHandler ChildMessage;

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_spawn_async (string working_directory, string[] argv, string[] envp, bool search_path, ChildSetupHandler child_setup, out int child_pid, out IntPtr status_channel, out IntPtr server_handle, ChildExitedHandler child_exited, ChildMessageHandler child_message, ChildCallbackHandler child_callback, out int standard_input, out int standard_output, out int standard_error, out IntPtr errout);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_send_command (IntPtr handle, ServerCommand command);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_server_read_uint64 (IntPtr handle, out long arg);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_read_memory (IntPtr handle, long start, int size, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_write_memory (IntPtr handle, IntPtr data, long start, int size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_target_info (IntPtr handle, out int target_int_size, out int target_long_size, out int target_address_size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_call_method (IntPtr handle, long method_address, long method_argument, long callback_argument);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_kill_process (int pid, bool force);

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		void check_error (CommandError error)
		{
			if (error == CommandError.NONE)
				return;

			handle_error (error);
		}

		void handle_error (CommandError error)
		{
			switch (error) {
			case CommandError.NOT_STOPPED:
				throw new TargetNotStoppedException ();

			default:
				throw new TargetException (
					"Got unknown error condition from inferior: " + error);
			}
		}

		void send_command (ServerCommand command)
		{
			CommandError result = mono_debugger_server_send_command (server_handle, command);

			check_error (result);
		}

		long read_long ()
		{
			long retval;
			if (!mono_debugger_server_read_uint64 (server_handle, out retval))
				throw new TargetException (
					"Can't read ulong argument from inferior");

			return retval;
		}

		internal TargetAsyncResult call_method (ITargetLocation method, long method_argument,
							TargetAsyncCallback callback, object user_data)
		{
			long number = ++last_callback_id;
			TargetAsyncResult async = new TargetAsyncResult (callback, user_data);
			pending_callbacks.Add (number, async);

			CommandError result = mono_debugger_server_call_method (
				server_handle, method.Location, method_argument, number);
			check_error (result);
			change_target_state (TargetState.RUNNING);
			return async;
		}

		public Inferior (string working_directory, string[] argv, string[] envp)
		{
			this.working_directory = working_directory;
			this.argv = argv;
			this.envp = envp;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr status_channel, error;

			string[] my_argv = new string [argv.Length + 5];
			my_argv [0] = "mono-debugger-server";
			my_argv [1] = OffsetTable.Magic.ToString ("x");
			my_argv [2] = OffsetTable.Version.ToString ();
			my_argv [3] = "0";
			my_argv [4] = working_directory;
			argv.CopyTo (my_argv, 5);

			bfd_symtab = new BfdSymbolTable (argv [0]);

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out server_handle,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				new ChildCallbackHandler (child_callback),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			if (!retval)
				throw new Exception ();

			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);

			setup_inferior ();
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

			bfd_symtab = new BfdSymbolTable (argv [0]);

			bool retval = mono_debugger_spawn_async (
				working_directory, my_argv, envp, true, null, out child_pid,
				out status_channel, out server_handle,
				new ChildExitedHandler (child_exited),
				new ChildMessageHandler (child_message),
				new ChildCallbackHandler (child_callback),
				out stdin_fd, out stdout_fd,
				out stderr_fd, out error);

			if (!retval)
				throw new Exception ();

			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);

			setup_inferior ();
		}

		void setup_inferior ()
		{
			inferior_stdout.ReadLine += new ReadLineHandler (inferior_output);
			inferior_stderr.ReadLine += new ReadLineHandler (inferior_errors);

			int target_int_size, target_long_size, target_address_size;
			CommandError result = mono_debugger_server_get_target_info
				(server_handle, out target_int_size, out target_long_size,
				 out target_address_size);
			check_error (result);

			target_info = new TargetInfo (target_int_size, target_long_size,
						      target_address_size);

			bfd_disassembler = bfd_symtab.GetDisassembler (this);
		}

		void read_mono_debugger_info ()
		{
			ITargetLocation symbol_info = bfd_symtab ["MONO_DEBUGGER__debugger_info"];
			if (symbol_info != null) {
				ITargetMemoryReader header = ReadMemory (symbol_info, 16);
				if (header.ReadLongInteger () != OffsetTable.Magic)
					throw new SymbolTableException ();
				if (header.ReadInteger () != OffsetTable.Version)
					throw new SymbolTableException ();

				int size = (int) header.ReadInteger ();

				ITargetMemoryReader table = ReadMemory (symbol_info, size);
				mono_debugger_info = new MonoDebuggerInfo (table);
				Console.WriteLine ("MONO DEBUGGER INFO: {0}", mono_debugger_info);
			}
		}

		void child_exited ()
		{
			child_pid = 0;
			if (ChildExited != null)
				ChildExited ();
		}

		void child_callback (long callback, long data)
		{
			child_message (ChildMessageType.CHILD_STOPPED, 0);

			if (!pending_callbacks.Contains (callback))
				return;

			TargetAsyncResult async = (TargetAsyncResult) pending_callbacks [callback];
			pending_callbacks.Remove (callback);

			async.Completed (data);
		}

		bool initialized;
		bool debugger_info_read;
		void child_message (ChildMessageType message, int arg)
		{
			switch (message) {
			case ChildMessageType.CHILD_STOPPED:
				if (!initialized) {
					Continue ();
					initialized = true;
					break;
				} else if (!debugger_info_read) {
					debugger_info_read = true;
					read_mono_debugger_info ();
				}
				change_target_state (TargetState.STOPPED);
				break;

			case ChildMessageType.CHILD_EXITED:
			case ChildMessageType.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED);
				break;

			default:
				Console.WriteLine ("CHILD MESSAGE: {0} {1}", message, arg);
				break;
			}

			if (ChildMessage != null)
				ChildMessage (message, arg);
		}

		void inferior_output (string line)
		{
			if (TargetOutput != null)
				TargetOutput (line);
		}

		void inferior_errors (string line)
		{
			if (TargetError != null)
				TargetError (line);
		}

		//
		// ITargetInfo
		//

		public int TargetIntegerSize {
			get {
				return target_info.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return target_info.TargetLongIntegerSize;
			}
		}

		public int TargetAddressSize {
			get {
				return target_info.TargetAddressSize;
			}
		}

		//
		// ITargetMemoryAccess
		//

		IntPtr read_buffer (ITargetLocation location, int size)
		{
			IntPtr data;
			CommandError result = mono_debugger_server_read_memory (
				server_handle, location.Address, size, out data);
			if (result != CommandError.NONE) {
				g_free (data);
				handle_error (result);
			}
			return data;
		}

		public byte[] ReadBuffer (ITargetLocation location, int size)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (location, size);
				byte[] retval = new byte [size];
				Marshal.Copy (data, retval, 0, size);
				return retval;
			} finally {
				g_free (data);
			}
		}

		public byte ReadByte (ITargetLocation location)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (location, 1);
				return Marshal.ReadByte (data);
			} finally {
				g_free (data);
			}
		}

		public int ReadInteger (ITargetLocation location)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (location, sizeof (int));
				return Marshal.ReadInt32 (data);
			} finally {
				g_free (data);
			}
		}

		public long ReadLongInteger (ITargetLocation location)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (location, sizeof (long));
				return Marshal.ReadInt64 (data);
			} finally {
				g_free (data);
			}
		}

		public ITargetLocation ReadAddress (ITargetLocation location)
		{
			switch (target_info.TargetAddressSize) {
			case 4:
				return new TargetLocation (ITargetMemoryAccess.ReadInteger (location));

			case 8:
				return new TargetLocation (ITargetMemoryAccess.ReadLongInteger (location));

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + target_info.TargetAddressSize);
			}
		}

		public string ReadString (ITargetLocation location)
		{
			StringBuilder sb = new StringBuilder ();

			ITargetLocation my_location = (ITargetLocation) location.Clone ();

			while (true) {
				byte b = ReadByte (my_location);

				if (b == 0)
					return sb.ToString ();

				sb.Append ((char) b);
				my_location.Offset++;
			}
		}

		public ITargetMemoryReader ReadMemory (ITargetLocation location, int size)
		{
			byte [] retval = ReadBuffer (location, size);
			return new TargetReader (retval, target_info);
		}

		public Stream GetMemoryStream (ITargetLocation location)
		{
			return new TargetMemoryStream (this, location, target_info);
		}

		public bool CanWrite {
			get {
				return false;
			}
		}

		public void WriteBuffer (ITargetLocation location, byte[] buffer, int size)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (buffer, 0, data, size);
				CommandError result = mono_debugger_server_write_memory (
					server_handle, data, location.Address, size);
				check_error (result);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteByte (ITargetLocation location, byte value)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (1);
				Marshal.WriteByte (data, value);
				CommandError result = mono_debugger_server_write_memory (
					server_handle, data, location.Address, 1);
				check_error (result);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteInteger (ITargetLocation location, int value)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (sizeof (int));
				Marshal.WriteInt32 (data, value);
				CommandError result = mono_debugger_server_write_memory (
					server_handle, data, location.Address, sizeof (int));
				if (data != IntPtr.Zero)
					check_error (result);
			} finally {
				Marshal.FreeHGlobal (data);
			}
		}

		public void WriteLongInteger (ITargetLocation location, long value)
		{
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (sizeof (long));
				Marshal.WriteInt64 (data, value);
				CommandError result = mono_debugger_server_write_memory (
					server_handle, data, location.Address, sizeof (long));
				check_error (result);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteAddress (ITargetLocation location, ITargetLocation address)
		{
			switch (target_info.TargetAddressSize) {
			case 4:
				WriteInteger (location, (int) address.Address);

			case 8:
				WriteLongInteger (location, address.Address);

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + target_info.TargetAddressSize);
			}
		}

		//
		// IInferior
		//

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				return target_state;
			}
		}

		void change_target_state (TargetState new_state)
		{
			if (new_state == target_state)
				return;

			target_state = new_state;

			if (StateChanged != null)
				StateChanged (target_state);
		}

		public void Continue ()
		{
			send_command (ServerCommand.CONTINUE);
			change_target_state (TargetState.RUNNING);
		}

		public void Detach ()
		{
			send_command (ServerCommand.DETACH);
		}

		public void Shutdown ()
		{
			send_command (ServerCommand.SHUTDOWN);
		}

		public void Kill ()
		{
			send_command (ServerCommand.KILL);
		}

		public void Step ()
		{
			send_command (ServerCommand.STEP);
			change_target_state (TargetState.RUNNING);
		}

		public void Next ()
		{
			ITargetLocation location = Frame ();
			location.Offset += bfd_disassembler.GetInstructionSize (location);

			Console.WriteLine ("TEST: {0}", location);
		}

		public ITargetLocation Frame ()
		{
			try {
				send_command (ServerCommand.GET_PC);
				return new TargetLocation (read_long ());
			} catch (TargetException e) {
				throw new NoStackException ();
			}
		}

		public IDisassembler Disassembler {
			get {
				return bfd_disassembler;
			}
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
					if (bfd_symtab != null)
						bfd_symtab.Dispose ();
					if (bfd_disassembler != null)
						bfd_disassembler.Dispose ();
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
