using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
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
	internal delegate void ChildCallbackHandler (long argument, long data, long data2);

	internal enum CommandError {
		NONE = 0,
		NO_INFERIOR,
		ALREADY_HAVE_INFERIOR,
		FORK,
		IO,
		UNKNOWN,
		INVALID_COMMAND,
		NOT_STOPPED,
		ALIGNMENT,
		RECURSIVE_CALL,
		NO_SUCH_BREAKPOINT,
		UNKNOWN_REGISTER
	}
	
	internal delegate void ChildSetupHandler ();

	internal class PTraceInferior : IInferior, IDisposable
	{
		IntPtr server_handle, g_source;
		IOOutputChannel inferior_stdin;
		IOInputChannel inferior_stdout;
		IOInputChannel inferior_stderr;

		string working_directory;
		string[] argv;
		string[] envp;

		Bfd bfd;
		BfdContainer bfd_container;
		BfdDisassembler bfd_disassembler;
		IArchitecture arch;
		SymbolTableCollection native_symtabs;
		SymbolTableCollection symtab_collection;
		ISymbolTable application_symtab;
		DebuggerBackend backend;

		int child_pid;
		bool native;

		ITargetInfo target_info;
		Hashtable pending_callbacks = new Hashtable ();
		long last_callback_id = 0;

		SingleSteppingEngine sse = null;

		public int PID {
			get {
				check_disposed ();
				return child_pid;
			}
		}

		public SingleSteppingEngine SingleSteppingEngine {
			get {
				return sse;
			}

			set {
				sse = value;
			}
		}

		public event TargetExitedHandler TargetExited;
		internal event ChildEventHandler ChildEvent;

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_spawn (IntPtr handle, string working_directory, string[] argv, string[] envp, bool search_path, TargetExitedHandler child_exited, ChildEventHandler child_event, ChildCallbackHandler child_callback, out int child_pid, out int standard_input, out int standard_output, out int standard_error, out IntPtr error);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_attach (IntPtr handle, int child_pid, TargetExitedHandler child_exited, ChildEventHandler child_event, ChildCallbackHandler child_callback);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_get_g_source (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_wait (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_pc (IntPtr handle, out long pc);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_current_insn_is_bpt (IntPtr handle, out int is_breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_step (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_continue (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_detach (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_finalize (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_read_memory (IntPtr handle, long start, int size, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_write_memory (IntPtr handle, IntPtr data, long start, int size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_target_info (IntPtr handle, out int target_int_size, out int target_long_size, out int target_address_size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_call_method (IntPtr handle, long method_address, long method_argument, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_call_method_1 (IntPtr handle, long method_address, long method_argument, string string_argument, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_call_method_invoke (IntPtr handle, long invoke_method, long method_address, long object_address, int num_params, IntPtr param_array, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_insert_breakpoint (IntPtr handle, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_remove_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_enable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_disable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_enable_breakpoints (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_disable_breakpoints (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_registers (IntPtr handle, int count, IntPtr registers, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_set_registers (IntPtr handle, int count, IntPtr registers, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_backtrace (IntPtr handle, int max_frames, long stop_address, out int count, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_ret_address (IntPtr handle, out long retval);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_stop (IntPtr handle);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_kill_process (int pid, bool force);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize ();

		[DllImport("glib-2.0")]
		extern static uint g_source_attach (IntPtr source, IntPtr context);

		[DllImport("glib-2.0")]
		extern static void g_source_destroy (IntPtr source);

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

			case CommandError.NO_INFERIOR:
				throw new NoTargetException ();

			case CommandError.ALREADY_HAVE_INFERIOR:
				throw new AlreadyHaveTargetException ();

			case CommandError.FORK:
				throw new CannotStartTargetException ();

			case CommandError.NO_SUCH_BREAKPOINT:
				throw new NoSuchBreakpointException ();

			case CommandError.UNKNOWN_REGISTER:
				throw new NoSuchRegisterException ();

			default:
				throw new InternalError ("Got unknown error condition from inferior: {0}",
							 error);
			}
		}

		TargetAsyncResult call_method (TargetAddress method, long method_argument,
					       TargetAsyncCallback callback, object user_data)
		{
			check_disposed ();
			long number = ++last_callback_id;
			TargetAsyncResult async = new TargetAsyncResult (callback, user_data);
			pending_callbacks.Add (number, async);

			TargetState old_state = change_target_state (TargetState.BUSY);
			try {
				check_error (mono_debugger_server_call_method (
					server_handle, method.Address, method_argument, number));
			} catch {
				change_target_state (old_state);
			}
			return async;
		}

		public long CallMethod (TargetAddress method, long method_argument)
		{
			check_disposed ();
			TargetAsyncResult result = call_method (
				method, method_argument, null, null);
			mono_debugger_server_wait (server_handle);
			if (!result.IsCompleted)
				throw new InternalError ("Call not completed");
			return (long) result.AsyncResult;
		}

		TargetAsyncResult call_method_1 (TargetAddress method, long method_argument,
						 string string_argument, TargetAsyncCallback callback,
						 object user_data)
		{
			check_disposed ();
			long number = ++last_callback_id;
			TargetAsyncResult async = new TargetAsyncResult (callback, user_data);
			pending_callbacks.Add (number, async);

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_call_method_1 (
					server_handle, method.Address, method_argument,
					string_argument, number));
			} catch {
				change_target_state (old_state);
			}
			return async;
		}

		public long CallStringMethod (TargetAddress method, long method_argument,
					      string string_argument)
		{
			check_disposed ();
			TargetAsyncResult result = call_method_1 (
				method, method_argument, string_argument, null, null);
			mono_debugger_server_wait (server_handle);
			if (!result.IsCompleted)
				throw new InternalError ("Call not completed");
			return (long) result.AsyncResult;
		}

		TargetAsyncResult call_method_invoke (TargetAddress invoke_method,
						      TargetAddress method_argument,
						      TargetAddress object_argument,
						      TargetAddress[] param_objects,
						      TargetAsyncCallback callback,
						      object user_data)
		{
			check_disposed ();
			long number = ++last_callback_id;
			TargetAsyncResult async = new TargetAsyncResult (callback, user_data);
			pending_callbacks.Add (number, async);

			int size = param_objects.Length;
			long[] param_addresses = new long [size];
			for (int i = 0; i < param_objects.Length; i++)
				param_addresses [i] = param_objects [i].Address;

			IntPtr data = IntPtr.Zero;
			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				if (size > 0) {
					data = Marshal.AllocHGlobal (size);
					Marshal.Copy (param_addresses, 0, data, size);
				}

				check_error (mono_debugger_server_call_method_invoke (
					server_handle, invoke_method.Address, method_argument.Address,
					object_argument.Address, size, data, number));
			} catch {
				change_target_state (old_state);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}

			return async;
		}

		public TargetAddress CallInvokeMethod (TargetAddress invoke_method,
						       TargetAddress method_argument,
						       TargetAddress object_argument,
						       TargetAddress[] param_objects,
						       out TargetAddress exc_object)
		{
			check_disposed ();
			TargetAsyncResult result = call_method_invoke (
				invoke_method, method_argument, object_argument, param_objects,
				null, null);
			mono_debugger_server_wait (server_handle);
			if (!result.IsCompleted)
				throw new InternalError ("Call not completed");

			long exc_addr = (long) result.AsyncResult2;
			long obj_addr = (long) result.AsyncResult;

			if (exc_addr != 0) {
				exc_object = new TargetAddress (object_argument.Domain, exc_addr);
				return TargetAddress.Null;
			}

			exc_object = TargetAddress.Null;
			if (obj_addr != 0)
				return new TargetAddress (object_argument.Domain, obj_addr);
			else
				return TargetAddress.Null;
		}

		public int InsertBreakpoint (TargetAddress address)
		{
			int retval;
			check_error (mono_debugger_server_insert_breakpoint (
				server_handle, address.Address, out retval));
			return retval;
		}

		public void RemoveBreakpoint (int breakpoint)
		{
			check_error (mono_debugger_server_remove_breakpoint (
				server_handle, breakpoint));
		}

		public void EnableBreakpoint (int breakpoint)
		{
			check_error (mono_debugger_server_enable_breakpoint (
				server_handle, breakpoint));
		}

		public void DisableBreakpoint (int breakpoint)
		{
			check_error (mono_debugger_server_disable_breakpoint (
				server_handle, breakpoint));
		}

		public void EnableAllBreakpoints ()
		{
			mono_debugger_server_enable_breakpoints (server_handle);
		}

		public void DisableAllBreakpoints ()
		{
			mono_debugger_server_disable_breakpoints (server_handle);
		}

		public DebuggerBackend DebuggerBackend {
			get {
				return backend;
			}
		}

		public PTraceInferior (DebuggerBackend backend, string working_directory,
				       string[] argv, string[] envp, bool native,
				       bool load_native_symtab, BfdContainer bfd_container,
				       DebuggerErrorHandler error_handler)
		{
			this.backend = backend;
			this.working_directory = working_directory;
			this.argv = argv;
			this.envp = envp;
			this.native = native;
			this.bfd_container = bfd_container;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr error;

			server_handle = mono_debugger_server_initialize ();
			if (server_handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");

			check_error (mono_debugger_server_spawn (
				server_handle, working_directory, argv, envp, true,
				new TargetExitedHandler (child_exited),
				new ChildEventHandler (child_event),
				new ChildCallbackHandler (child_callback),
				out child_pid, out stdin_fd, out stdout_fd, out stderr_fd,
				out error));

			inferior_stdin = new IOOutputChannel (stdin_fd);
			inferior_stdout = new IOInputChannel (stdout_fd);
			inferior_stderr = new IOInputChannel (stderr_fd);

			try {
				bfd = bfd_container.AddFile (this, argv [0], load_native_symtab);
				if (load_native_symtab)
					bfd.ReadDwarf ();
			} catch (Exception e) {
				error_handler (this, String.Format (
					"Can't read symbol file {0}", argv [0]), e);
			}

			setup_inferior (load_native_symtab);
		}

		public PTraceInferior (DebuggerBackend backend, int pid, string[] envp,
				       bool load_native_symtab, BfdContainer bfd_container,
				       DebuggerErrorHandler error_handler)
		{
			this.backend = backend;
			this.envp = envp;
			this.bfd_container = bfd_container;

			server_handle = mono_debugger_server_initialize ();
			if (server_handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");

			check_error (mono_debugger_server_attach (
				server_handle, pid, new TargetExitedHandler (child_exited),
				new ChildEventHandler (child_event),
				new ChildCallbackHandler (child_callback)));

			try {
				bfd = bfd_container.AddFile (this, argv [0], load_native_symtab);
				if (load_native_symtab)
					bfd.ReadDwarf ();
			} catch (Exception e) {
				error_handler (this, String.Format (
					"Can't read symbol file {0}", argv [0]), e);
			}

			setup_inferior (load_native_symtab);
		}

		void setup_inferior (bool load_native_symtab)
		{
			inferior_stdout.ReadLine += new ReadLineHandler (inferior_output);
			inferior_stderr.ReadLine += new ReadLineHandler (inferior_errors);

			g_source = mono_debugger_server_get_g_source (server_handle);
			if (g_source == IntPtr.Zero)
				handle_error (CommandError.UNKNOWN);

			g_source_attach (g_source, IntPtr.Zero);

			int target_int_size, target_long_size, target_address_size;
			check_error (mono_debugger_server_get_target_info
				(server_handle, out target_int_size, out target_long_size,
				 out target_address_size));

			target_info = new TargetInfo (target_int_size, target_long_size,
						      target_address_size);

			bfd_disassembler = bfd.GetDisassembler (this);
			arch = new ArchitectureI386 (this);

			native_symtabs = new SymbolTableCollection ();

			if (load_native_symtab) {
				try {
					ISymbolTable bfd_symtab = bfd.SymbolTable;
					if (bfd_symtab != null)
						native_symtabs.AddSymbolTable (bfd_symtab);
				} catch (Exception e) {
					Console.WriteLine ("Can't get native symbol table: {0}", e);
				}
			}

			update_symtabs ();
		}

		public void UpdateModules ()
		{
			bfd.UpdateSharedLibraryInfo ();
		}

		void update_symtabs ()
		{
			symtab_collection = new SymbolTableCollection ();
			symtab_collection.AddSymbolTable (native_symtabs);
			symtab_collection.AddSymbolTable (application_symtab);

			bfd_disassembler.SymbolTable = symtab_collection;
		}

		public TargetAddress SimpleLookup (string name)
		{
			return bfd [name];
		}

		public TargetAddress MainMethodAddress {
			get {
				return bfd ["main"];
			}
		}

		void child_exited ()
		{
			child_pid = 0;
			Dispose ();
			if (TargetExited != null)
				TargetExited ();
		}

		void child_callback (long callback, long data, long data2)
		{
			change_target_state (TargetState.STOPPED);

			if (!pending_callbacks.Contains (callback))
				return;

			TargetAsyncResult async = (TargetAsyncResult) pending_callbacks [callback];
			pending_callbacks.Remove (callback);

			async.Completed (data, data2);

			child_event (ChildEventType.CHILD_CALLBACK, 0);
		}

		void child_event (ChildEventType message, int arg)
		{
			switch (message) {
			case ChildEventType.CHILD_STOPPED:
				change_target_state (TargetState.STOPPED, arg);
				break;

			case ChildEventType.CHILD_EXITED:
			case ChildEventType.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED, arg);
				break;

			case ChildEventType.CHILD_HIT_BREAKPOINT:
				change_target_state (TargetState.STOPPED, 0);
				break;

			default:
				break;
			}

			if (ChildEvent != null)
				ChildEvent (message, arg);
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

		void debugger_output (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
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

		IntPtr read_buffer (TargetAddress address, int size)
		{
			IntPtr data;
			CommandError result = mono_debugger_server_read_memory (
				server_handle, address.Address, size, out data);
			if (result != CommandError.NONE) {
				g_free (data);
				handle_error (result);
				throw new Exception ("Internal error: this line will never be reached");
			}
			return data;
		}

		public byte[] ReadBuffer (TargetAddress address, int size)
		{
			check_disposed ();
			if (size == 0)
				return new byte [0];
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (address, size);
				byte[] retval = new byte [size];
				Marshal.Copy (data, retval, 0, size);
				return retval;
			} finally {
				g_free (data);
			}
		}

		public byte ReadByte (TargetAddress address)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (address, 1);
				return Marshal.ReadByte (data);
			} finally {
				g_free (data);
			}
		}

		public int ReadInteger (TargetAddress address)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (address, 4);
				return Marshal.ReadInt32 (data);
			} finally {
				g_free (data);
			}
		}

		public long ReadLongInteger (TargetAddress address)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = read_buffer (address, 8);
				return Marshal.ReadInt64 (data);
			} finally {
				g_free (data);
			}
		}

		public TargetAddress ReadAddress (TargetAddress address)
		{
			check_disposed ();
			switch (TargetAddressSize) {
			case 4:
				return new TargetAddress (this, (uint) ReadInteger (address));

			case 8:
				return new TargetAddress (this, ReadLongInteger (address));

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
			}
		}

		public string ReadString (TargetAddress address)
		{
			check_disposed ();
			StringBuilder sb = new StringBuilder ();

			while (true) {
				byte b = ReadByte (address);
				address++;

				if (b == 0)
					return sb.ToString ();

				sb.Append ((char) b);
			}
		}

		public ITargetMemoryReader ReadMemory (TargetAddress address, int size)
		{
			check_disposed ();
			byte [] retval = ReadBuffer (address, size);
			return new TargetReader (retval, this);
		}

		public bool CanWrite {
			get {
				return false;
			}
		}

		public void WriteBuffer (TargetAddress address, byte[] buffer, int size)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (buffer, 0, data, size);
				check_error (mono_debugger_server_write_memory (
					server_handle, data, address.Address, size));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteByte (TargetAddress address, byte value)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (1);
				Marshal.WriteByte (data, value);
				check_error (mono_debugger_server_write_memory (
					server_handle, data, address.Address, 1));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteInteger (TargetAddress address, int value)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (4);
				Marshal.WriteInt32 (data, value);
				check_error (mono_debugger_server_write_memory (
					server_handle, data, address.Address, 4));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteLongInteger (TargetAddress address, long value)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (8);
				Marshal.WriteInt64 (data, value);
				check_error (mono_debugger_server_write_memory (
					server_handle, data, address.Address, 8));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public void WriteAddress (TargetAddress address, TargetAddress value)
		{
			check_disposed ();
			switch (TargetAddressSize) {
			case 4:
				WriteInteger (address, (int) value.Address);
				break;

			case 8:
				WriteLongInteger (address, value.Address);
				break;

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
			}
		}

		//
		// IInferior
		//

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event TargetOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;
		public event StateChangedHandler StateChanged;

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				check_disposed ();
				return target_state;
			}
		}

		TargetState change_target_state (TargetState new_state)
		{
			return change_target_state (new_state, 0);
		}

		TargetState change_target_state (TargetState new_state, int arg)
		{
			if (new_state == target_state)
				return target_state;

			TargetState old_state = target_state;
			target_state = new_state;

			if (StateChanged != null)
				StateChanged (target_state, arg);

			return old_state;
		}

		public void Step ()
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_step (server_handle));
			} catch {
				change_target_state (old_state);
			}
		}

		public void Continue ()
		{
			check_disposed ();
			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_continue (server_handle));
			} catch {
				change_target_state (old_state);
			}
		}

		public void Stop ()
		{
			check_disposed ();
			check_error (mono_debugger_server_stop (server_handle));
		}

		public void Detach ()
		{
			check_disposed ();
			check_error (mono_debugger_server_detach (server_handle));
		}

		public void Shutdown ()
		{
			// send_command (ServerCommand.SHUTDOWN);
		}

		public void Kill ()
		{
			// send_command (ServerCommand.KILL);
		}

		public TargetAddress CurrentFrame {
			get {
				long pc;
				check_disposed ();
				CommandError result = mono_debugger_server_get_pc (server_handle, out pc);
				if (result != CommandError.NONE)
					throw new NoStackException ();

				return new TargetAddress (this, pc);
			}
		}

		public bool CurrentInstructionIsBreakpoint {
			get {
				check_disposed ();
				int is_breakpoint;
				CommandError result = mono_debugger_server_current_insn_is_bpt (
					server_handle, out is_breakpoint);
				if (result != CommandError.NONE)
					throw new NoStackException ();

				return is_breakpoint != 0;
			}
		}

		public IDisassembler Disassembler {
			get {
				check_disposed ();
				return bfd_disassembler;
			}
		}

		public ISymbolTable SymbolTable {
			get {
				check_disposed ();
				return native_symtabs;
			}
		}

		public ISymbolTable ApplicationSymbolTable {
			get {
				check_disposed ();
				return application_symtab;
			}

			set {
				check_disposed ();
				application_symtab = value;
				update_symtabs ();
			}
		}

		public IArchitecture Architecture {
			get {
				check_disposed ();
				return arch;
			}
		}

		public Module[] Modules {
			get {
				return new Module[] { bfd.Module };
			}
		}

		public long GetRegister (int register)
		{
			long[] retval = GetRegisters (new int[] { register });
			return retval [0];
		}

		public long[] GetRegisters (int[] registers)
		{
			IntPtr data = IntPtr.Zero, buffer = IntPtr.Zero;
			try {
				int size = registers.Length * 4;
				int buffer_size = registers.Length * 8;
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (registers, 0, data, registers.Length);
				buffer = Marshal.AllocHGlobal (buffer_size);
				CommandError result = mono_debugger_server_get_registers (
					server_handle, registers.Length, data, buffer);
				check_error (result);
				long[] retval = new long [registers.Length];
				Marshal.Copy (buffer, retval, 0, registers.Length);
				return retval;
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public void SetRegister (int register, long value)
		{
			SetRegisters (new int[] { register }, new long[] { value });
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			IntPtr data = IntPtr.Zero, buffer = IntPtr.Zero;
			try {
				int size = registers.Length * 4;
				int buffer_size = registers.Length * 8;
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (registers, 0, data, registers.Length);
				buffer = Marshal.AllocHGlobal (buffer_size);
				Marshal.Copy (values, 0, buffer, registers.Length);
				CommandError result = mono_debugger_server_set_registers (
					server_handle, registers.Length, data, buffer);
				check_error (result);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		private struct ServerStackFrame
		{
			public long Address;
			public long ParamsAddress;
			public long LocalsAddress;
		}

		private class InferiorStackFrame : IInferiorStackFrame
		{
			IInferior inferior;
			ServerStackFrame frame;

			public InferiorStackFrame (IInferior inferior, ServerStackFrame frame)
			{
				this.inferior = inferior;
				this.frame = frame;
			}

			public IInferior Inferior {
				get {
					return inferior;
				}
			}

			public TargetAddress Address {
				get {
					return new TargetAddress (inferior, frame.Address);
				}
			}

			public TargetAddress ParamsAddress {
				get {
					return new TargetAddress (inferior, frame.ParamsAddress);
				}
			}

			public TargetAddress LocalsAddress {
				get {
					return new TargetAddress (inferior, frame.LocalsAddress);
				}
			}
		}

		private delegate void TargetAsyncCallback (object user_data, object result, object result2);

		private class TargetAsyncResult
		{
			object user_data, result, result2;
			bool completed;
			TargetAsyncCallback callback;

			public TargetAsyncResult (TargetAsyncCallback callback, object user_data)
			{
				this.callback = callback;
				this.user_data = user_data;
			}

			public void Completed (object result, object result2)
			{
				if (completed)
					throw new InvalidOperationException ();

				completed = true;
				if (callback != null)
					callback (user_data, result, result2);

				this.result = result;
				this.result2 = result2;
			}

			public object AsyncResult {
				get {
					return result;
				}
			}

			public object AsyncResult2 {
				get {
					return result2;
				}
			}

			public bool IsCompleted {
				get {
					return completed;
				}
			}
		}

		public IInferiorStackFrame[] GetBacktrace (int max_frames, TargetAddress stop)
		{
			IntPtr data = IntPtr.Zero;
			try {
				int count;

				long stop_addr = 0;
				if (!stop.IsNull)
					stop_addr = stop.Address;
				CommandError result = mono_debugger_server_get_backtrace (
					server_handle, max_frames, stop_addr, out count, out data);
				check_error (result);

				ServerStackFrame[] frames = new ServerStackFrame [count];
				IntPtr temp = data;
				for (int i = 0; i < count; i++) {
					frames [i] = (ServerStackFrame) Marshal.PtrToStructure (
						temp, typeof (ServerStackFrame));
					temp = new IntPtr ((long) temp + Marshal.SizeOf (frames [i]));
				}

				IInferiorStackFrame[] retval = new IInferiorStackFrame [count];
				for (int i = 0; i < count; i++)
					retval [i] = new InferiorStackFrame (this, frames [i]);
				return retval;
			} finally {
				g_free (data);
			}
		}

		public TargetAddress GetReturnAddress ()
		{
			long address;
			CommandError result = mono_debugger_server_get_ret_address (
					server_handle, out address);
			check_error (result);

			return new TargetAddress (this, address);
		}

		//
		// IDisposable
		//

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Inferior");
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					bfd_container.CloseBfd (bfd);
					if (bfd_disassembler != null)
						bfd_disassembler.Dispose ();
					// Do stuff here
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					if (g_source != IntPtr.Zero) {
						g_source_destroy (g_source);
						g_source = IntPtr.Zero;
					}
					if (server_handle != IntPtr.Zero) {
						mono_debugger_server_finalize (server_handle);
						server_handle = IntPtr.Zero;
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

		~PTraceInferior ()
		{
			Dispose (false);
		}
	}
}
