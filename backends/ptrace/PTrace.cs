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
		IntPtr server_handle;
		IOOutputChannel inferior_stdin;
		IOInputChannel inferior_stdout;
		IOInputChannel inferior_stderr;

		ProcessStart start;

		Bfd bfd;
		BfdContainer bfd_container;
		BfdDisassembler bfd_disassembler;
		IArchitecture arch;
		SymbolTableCollection symtab_collection;
		DebuggerBackend backend;
		DebuggerErrorHandler error_handler;
		BreakpointManager breakpoint_manager;
		ThreadManager thread_manager;

		int child_pid;
		bool native;
		bool initialized;

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
		static extern CommandError mono_debugger_server_spawn (IntPtr handle, string working_directory, string[] argv, string[] envp, bool search_path, out int child_pid, out int standard_input, out int standard_output, out int standard_error, out IntPtr error);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_attach (IntPtr handle, int child_pid);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_wait (IntPtr handle, out ChildEventType message, out long arg, out long data1, out long data2);

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
		static extern CommandError mono_debugger_server_insert_hw_breakpoint (IntPtr handle, int index, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_remove_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_enable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_disable_breakpoint (IntPtr handle, int breakpoint);

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

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_set_signal (IntPtr handle, int signal, int send_it);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_kill (IntPtr handle);

		[DllImport("monodebuggerglue")]
		static extern void mono_debugger_glue_kill_process (int pid, bool force);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize (IntPtr breakpoint_manager);

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

		public long CallMethod (TargetAddress method, long method_argument)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.BUSY);
			try {
				check_error (mono_debugger_server_call_method (
					server_handle, method.Address, method_argument, 0));
			} catch {
				change_target_state (old_state);
			}

			ChildEvent cevent = WaitForCallback ();
			return cevent.Data1;
		}

		public long CallStringMethod (TargetAddress method, long method_argument,
					      string string_argument)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_call_method_1 (
					server_handle, method.Address, method_argument,
					string_argument, 0));
			} catch {
				change_target_state (old_state);
			}

			ChildEvent cevent = WaitForCallback ();
			return cevent.Data1;
		}

		ChildEvent call_method_invoke (TargetAddress invoke_method,
					       TargetAddress method_argument,
					       TargetAddress object_argument,
					       TargetAddress[] param_objects)
		{
			check_disposed ();

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
					object_argument.Address, size, data, 0));
			} catch {
				change_target_state (old_state);
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}

			return WaitForCallback ();
		}

		public TargetAddress CallInvokeMethod (TargetAddress invoke_method,
						       TargetAddress method_argument,
						       TargetAddress object_argument,
						       TargetAddress[] param_objects,
						       out TargetAddress exc_object)
		{
			check_disposed ();
			ChildEvent cevent = call_method_invoke (
				invoke_method, method_argument, object_argument, param_objects);

			long exc_addr = cevent.Data2;
			long obj_addr = cevent.Data1;

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

		public int InsertHardwareBreakpoint (TargetAddress address, int index)
		{
			int retval;
			check_error (mono_debugger_server_insert_hw_breakpoint (
				server_handle, index, address.Address, out retval));
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

		public DebuggerBackend DebuggerBackend {
			get {
				return backend;
			}
		}

		public PTraceInferior (DebuggerBackend backend, ProcessStart start,
				       BfdContainer bfd_container, BreakpointManager breakpoint_manager,
				       DebuggerErrorHandler error_handler)
		{
			this.backend = backend;
			this.start = start;
			this.native = !(start is ManagedProcessStart);
			this.bfd_container = bfd_container;
			this.error_handler = error_handler;
			this.breakpoint_manager = breakpoint_manager;

			thread_manager = backend.ThreadManager;
			arch = new ArchitectureI386 (this, thread_manager);

			server_handle = mono_debugger_server_initialize (breakpoint_manager.Manager);
			if (server_handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");
		}

		public void Run ()
		{
			if (initialized)
				throw new AlreadyHaveTargetException ();

			initialized = true;

			int stdin_fd, stdout_fd, stderr_fd;
			IntPtr error;

			check_error (mono_debugger_server_spawn (
				server_handle, start.WorkingDirectory, start.CommandLineArguments,
				start.Environment, true,
				out child_pid, out stdin_fd, out stdout_fd, out stderr_fd,
				out error));

			inferior_stdin = new IOOutputChannel (stdin_fd, false, false);
			inferior_stdout = new IOInputChannel (stdout_fd, true, false);
			inferior_stderr = new IOInputChannel (stderr_fd, true, false);

			setup_inferior (start, error_handler);
			change_target_state (TargetState.STOPPED, 0);
		}

		public void Attach (int pid)
		{
			if (initialized)
				throw new AlreadyHaveTargetException ();

			initialized = true;

			check_error (mono_debugger_server_attach (server_handle, pid));

			setup_inferior (start, error_handler);
			change_target_state (TargetState.STOPPED, 0);
		}

		public ChildEvent Wait ()
		{
			long arg, data1, data2;
			ChildEventType message;

			mono_debugger_server_wait (server_handle, out message, out arg, out data1, out data2);
			if (message == ChildEventType.CHILD_CALLBACK)
				return new ChildEvent (arg, data1, data2);

			if ((message == ChildEventType.CHILD_EXITED) ||
			    (message == ChildEventType.CHILD_SIGNALED))
				child_exited ();

			return new ChildEvent (message, (int) arg);
		}

		public ChildEvent WaitForCallback ()
		{
		again:
			ChildEvent cevent = Wait ();

			if (cevent == null)
				goto again;
			else if (cevent.Type != ChildEventType.CHILD_CALLBACK)
				throw new InternalError ("Call not completed");

			return cevent;
		}

		void setup_inferior (ProcessStart start, DebuggerErrorHandler error_handler)
		{
			try {
				bfd = bfd_container.AddFile (this, start.TargetApplication,
							     start.LoadNativeSymtab);
				if (start.LoadNativeSymtab)
					bfd.ReadDwarf ();
			} catch (Exception e) {
				error_handler (this, String.Format (
					"Can't read symbol file {0}", start.TargetApplication), e);
			}

			if (inferior_stdout != null) {
				inferior_stdout.ReadLineEvent += new ReadLineHandler (inferior_output);
				inferior_stderr.ReadLineEvent += new ReadLineHandler (inferior_errors);
			}

			int target_int_size, target_long_size, target_address_size;
			check_error (mono_debugger_server_get_target_info
				(server_handle, out target_int_size, out target_long_size,
				 out target_address_size));

			target_info = new TargetInfo (target_int_size, target_long_size,
						      target_address_size);

			bfd_disassembler = bfd.GetDisassembler (this);

			if (start.LoadNativeSymtab) {
				try {
					ISymbolTable bfd_symtab = bfd.SymbolTable;
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

		public int StopSignal {
			get {
				// FIXME: Return SIGSTOP
				return 19;
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

		public TargetAddress ReadGlobalAddress (TargetAddress address)
		{
			check_disposed ();
			switch (TargetAddressSize) {
			case 4:
				return new TargetAddress (thread_manager, (uint) ReadInteger (address));

			case 8:
				return new TargetAddress (thread_manager, ReadLongInteger (address));

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

		public ITargetMemoryReader ReadMemory (byte[] buffer)
		{
			check_disposed ();
			return new TargetReader (buffer, this);
		}

		public bool CanWrite {
			get {
				return true;
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
				OnMemoryChanged ();
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
				OnMemoryChanged ();
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
				OnMemoryChanged ();
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
				OnMemoryChanged ();
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

		public void SetSignal (int signal, bool send_it)
		{
			check_disposed ();
			int do_send = send_it ? 1 : 0;
			check_error (mono_debugger_server_set_signal (server_handle, signal, do_send));
		}

		public void Detach ()
		{
			check_disposed ();
			check_error (mono_debugger_server_detach (server_handle));
		}

		public void Shutdown ()
		{
			mono_debugger_server_kill (server_handle);
		}

		public void Kill ()
		{
			mono_debugger_server_kill (server_handle);
		}

		public TargetAddress CurrentFrame {
			get {
				long pc;
				check_disposed ();
				CommandError result = mono_debugger_server_get_pc (server_handle, out pc);
				if (result != CommandError.NONE)
					throw new NoStackException ();

				return new TargetAddress (thread_manager, pc);
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
			PTraceInferior inferior;
			ServerStackFrame frame;

			public InferiorStackFrame (PTraceInferior inferior, ServerStackFrame frame)
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
					return new TargetAddress (inferior.thread_manager, frame.Address);
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

			return new TargetAddress (thread_manager, address);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			// We cannot use System.IO to read this file because it is not
			// seekable.  Actually, the file is seekable, but it contains
			// "holes" and each line starts on a new 4096 bytes block.
			// So if you just read the first line from the file, the current
			// file position will be rounded up to the next 4096 bytes
			// boundary - it'll be different from what System.IO thinks is
			// the current file position and System.IO will try to "fix" this
			// by seeking back.
			string mapfile = String.Format ("/proc/{0}/maps", child_pid);
			string contents = FileUtils.GetFileContents (mapfile);

			ArrayList list = new ArrayList ();

			using (StringReader reader = new StringReader (contents)) {
				do {
					string l = reader.ReadLine ();
					if (l == null)
						break;

					bool is64bit;
					if (l [8] == '-')
						is64bit = false;
					else if (l [16] == '-')
						is64bit = true;
					else
						throw new InternalError ();

					string sstart = is64bit ? l.Substring (0,16) : l.Substring (0,8);
					string send = is64bit ? l.Substring (17,16) : l.Substring (9,8);
					string sflags = is64bit ? l.Substring (34,4) : l.Substring (18,4);

					long start = Int64.Parse (sstart, NumberStyles.HexNumber);
					long end = Int64.Parse (send, NumberStyles.HexNumber);

					string name;
					if (is64bit)
						name = (l.Length > 73) ? l.Substring (73) : "";
					else
						name = (l.Length > 49) ? l.Substring (49) : "";
					name = name.TrimStart (' ').TrimEnd (' ');
					if (name == "")
						name = null;

					TargetMemoryFlags flags = 0;
					if (sflags [1] != 'w')
						flags |= TargetMemoryFlags.ReadOnly;

					TargetMemoryArea area = new TargetMemoryArea (
						new TargetAddress (thread_manager, start),
						new TargetAddress (thread_manager, end),
						flags, name, this);
					list.Add (area);
				} while (true);
			}

			TargetMemoryArea[] maps = new TargetMemoryArea [list.Count];
			list.CopyTo (maps, 0);
			return maps;
		}

		protected virtual void OnMemoryChanged ()
		{
			// child_event (ChildEventType.CHILD_MEMORY_CHANGED, 0);
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
