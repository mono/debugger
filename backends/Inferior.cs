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
	internal delegate void ChildOutputHandler (string output);

	internal abstract class Inferior : ITargetAccess, ITargetNotification, IDisposable
	{
		protected IntPtr server_handle;
		protected Bfd bfd;
		protected BfdDisassembler bfd_disassembler;
		protected AddressDomain address_domain;
		protected ThreadManager thread_manager;

		protected readonly ProcessStart start;

		protected readonly BfdContainer bfd_container;
		protected readonly IArchitecture arch;
		protected readonly SymbolTableCollection symtab_collection;
		protected readonly DebuggerBackend backend;
		protected readonly DebuggerErrorHandler error_handler;
		protected readonly BreakpointManager breakpoint_manager;
		protected readonly AddressDomain global_address_domain;
		protected readonly bool native;

		int child_pid, tid;
		bool initialized;

		ITargetInfo target_info;
		Hashtable pending_callbacks = new Hashtable ();
		long last_callback_id = 0;

		SingleSteppingEngine sse = null;

		public bool HasTarget {
			get {
				return initialized;
			}
		}

		public int PID {
			get {
				check_disposed ();
				return child_pid;
			}
		}

		public int TID {
			get {
				check_disposed ();
				return tid;
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

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_spawn (IntPtr handle, string working_directory, string[] argv, string[] envp, out int child_pid, ChildOutputHandler stdout_handler, ChildOutputHandler stderr_handler, out IntPtr error, out bool has_thread_manager);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_attach (IntPtr handle, int child_pid, out int tid);

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
		static extern CommandError mono_debugger_server_read_memory (IntPtr handle, long start, int size, IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_write_memory (IntPtr handle, IntPtr data, long start, int size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_get_target_info (IntPtr handle, out int target_int_size, out int target_long_size, out int target_address_size);

		[DllImport("monodebuggerserver")]
		static extern CommandError mono_debugger_server_call_method (IntPtr handle, long method_address, long method_argument1, long method_argument2, long callback_argument);

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

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize (IntPtr breakpoint_manager);

		public enum ChildEventType {
			CHILD_EXITED = 1,
			CHILD_STOPPED,
			CHILD_SIGNALED,
			CHILD_CALLBACK,
			CHILD_HIT_BREAKPOINT,
			CHILD_MEMORY_CHANGED,
			CHILD_CREATED_THREAD
		}

		public delegate void ChildEventHandler (ChildEventType message, int arg);

		public sealed class ChildEvent
		{
			public readonly ChildEventType Type;
			public readonly long Argument;

			public readonly long Data1;
			public readonly long Data2;

			public ChildEvent (ChildEventType type, long arg,
					   long data1, long data2)
			{
				this.Type = type;
				this.Argument = arg;
				this.Data1 = data1;
				this.Data2 = data2;
			}

			public override string ToString ()
			{
				return String.Format ("ChildEvent ({0}:{1}:{2:x}:{3:x})",
						      Type, Argument, Data1, Data2);
			}
		}

		protected enum CommandError {
			None = 0,
			Unknown,
			NoInferior,
			AlreadyHaveInferior,
			Fork,
			NotStopped,
			RecursiveCall,
			NoSuchBreakpoint,
			UnknownRegister,
			DrOccupied,
			MemoryAccess
		}

		protected Inferior (DebuggerBackend backend, ProcessStart start,
				    BreakpointManager bpm,
				    DebuggerErrorHandler error_handler,
				    AddressDomain global_address_domain)
		{
			this.backend = backend;
			this.start = start;
			this.native = !(start is ManagedProcessStart);
			this.bfd_container = backend.BfdContainer;
			this.error_handler = error_handler;
			this.breakpoint_manager = bpm;
			this.global_address_domain = global_address_domain;

			arch = new ArchitectureI386 ();

			server_handle = mono_debugger_server_initialize (breakpoint_manager.Manager);
			if (server_handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");
		}

		public static Inferior CreateInferior (ThreadManager thread_manager,
						       ProcessStart start)
		{
			BreakpointManager bpm = new BreakpointManager ();
			return new PTraceInferior (
				thread_manager.DebuggerBackend, start, bpm, null,
				thread_manager.AddressDomain, null);
		}

		public abstract Inferior CreateThread ();

		[DllImport("glib-2.0")]
		extern static void g_free (IntPtr data);

		protected void check_error (CommandError error)
		{
			if (error == CommandError.None)
				return;

			handle_error (error);
		}

		void handle_error (CommandError error)
		{
			switch (error) {
			case CommandError.NotStopped:
				throw new TargetNotStoppedException ();

			case CommandError.NoInferior:
				throw new NoTargetException ();

			case CommandError.AlreadyHaveInferior:
				throw new AlreadyHaveTargetException ();

			case CommandError.Fork:
				throw new CannotStartTargetException ();

			case CommandError.NoSuchBreakpoint:
				throw new NoSuchBreakpointException ();

			case CommandError.UnknownRegister:
				throw new NoSuchRegisterException ();

			default:
				throw new InternalError ("Got unknown error condition from inferior: {0}",
							 error);
			}
		}

		public void CallMethod (TargetAddress method, long data1, long data2,
					long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.BUSY);
			try {
				check_error (mono_debugger_server_call_method (
					server_handle, method.Address, data1, data2,
					callback_arg));
			} catch {
				change_target_state (old_state);
			}
		}

		public void CallMethod (TargetAddress method, long arg1, string arg2,
					long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_call_method_1 (
					server_handle, method.Address, arg1,
					arg2, callback_arg));
			} catch {
				change_target_state (old_state);
			}
		}

		public void RuntimeInvoke (TargetAddress invoke_method, TargetAddress method_argument,
					   TargetAddress object_argument, TargetAddress[] param_objects,
					   long callback_arg)
		{
			check_disposed ();

			int size = param_objects.Length;
			long[] param_addresses = new long [size];
			for (int i = 0; i < param_objects.Length; i++)
				param_addresses [i] = param_objects [i].Address;

			IntPtr data = IntPtr.Zero;
			try {
				if (size > 0) {
					data = Marshal.AllocHGlobal (size * 8);
					Marshal.Copy (param_addresses, 0, data, size);
				}

				check_error (mono_debugger_server_call_method_invoke (
					server_handle, invoke_method.Address, method_argument.Address,
					object_argument.Address, size, data, callback_arg));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		TargetAddress ITargetAccess.CallMethod (TargetAddress method, string argument)
		{
			throw new InvalidOperationException ();
		}

		TargetAddress ITargetAccess.CallMethod (TargetAddress method, TargetAddress arg1, TargetAddress arg2)
		{
			throw new InvalidOperationException ();
		}

		TargetAddress ITargetAccess.RuntimeInvoke (TargetAddress invoke_address, TargetAddress method_argument,
							   TargetAddress object_argument, TargetAddress[] param_objects,
							   out TargetAddress exc_object)
		{
			throw new InvalidOperationException ();
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

		public ProcessStart ProcessStart {
			get {
				return start;
			}
		}

		public BfdContainer BfdContainer {
			get {
				return bfd_container;
			}
		}

		public void Run (bool redirect_fds)
		{
			if (initialized)
				throw new AlreadyHaveTargetException ();

			initialized = true;

			IntPtr error;

			bool has_thread_manager;
			CommandError result = mono_debugger_server_spawn (
				server_handle, start.WorkingDirectory, start.CommandLineArguments,
				start.Environment, out child_pid,
				new ChildOutputHandler (inferior_stdout_handler),
				new ChildOutputHandler (inferior_stderr_handler),
				out error, out has_thread_manager);
			if (result != CommandError.None) {
				string message = Marshal.PtrToStringAuto (error);
				g_free (error);

				throw new CannotStartTargetException (message);
			}

			SetupInferior ();

			change_target_state (TargetState.STOPPED, 0);
		}

		public void Attach (int pid)
		{
			if (initialized)
				throw new AlreadyHaveTargetException ();

			initialized = true;

			check_error (mono_debugger_server_attach (server_handle, pid, out tid));
			this.child_pid = pid;

			SetupInferior ();
			change_target_state (TargetState.STOPPED, 0);
		}

		public abstract ChildEvent Wait ();

		public abstract ChildEvent ProcessEvent (long status);

		protected virtual void SetupInferior ()
		{
			address_domain = new AddressDomain (String.Format ("ptrace ({0})", child_pid));

			int target_int_size, target_long_size, target_address_size;
			check_error (mono_debugger_server_get_target_info
				(server_handle, out target_int_size, out target_long_size,
				 out target_address_size));

			target_info = new TargetInfo (target_int_size, target_long_size, target_address_size);

			try {
				bfd = bfd_container.AddFile (this, start.TargetApplication,
							     start.LoadNativeSymtab);
			} catch (Exception e) {
				error_handler (this, String.Format (
					"Can't read symbol file {0}", start.TargetApplication), e);
				return;
			}

			bfd_disassembler = bfd.GetDisassembler (this);
		}

		public void UpdateModules ()
		{
			bfd.UpdateSharedLibraryInfo (this);
		}

		public Bfd Bfd {
			get { return bfd; }
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

		protected void child_exited ()
		{
			child_pid = 0;
			Dispose ();
			if (TargetExited != null)
				TargetExited ();
		}

		void inferior_stdout_handler (string line)
		{
			if (TargetOutput != null)
				TargetOutput (false, line);
		}

		void inferior_stderr_handler (string line)
		{
			if (TargetOutput != null)
				TargetOutput (true, line);
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

		public AddressDomain AddressDomain {
			get { 
				if (address_domain == null)
					throw new NoTargetException ();

				return address_domain;
			}
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return global_address_domain;
			}
		}

		IntPtr read_buffer (TargetAddress address, int size)
		{
			IntPtr data = Marshal.AllocHGlobal (size);
			CommandError result = mono_debugger_server_read_memory (
				server_handle, address.Address, size, data);
			if (result == CommandError.MemoryAccess) {
				Marshal.FreeHGlobal (data);
				throw new TargetMemoryException (address, size);
			} else if (result != CommandError.None) {
				Marshal.FreeHGlobal (data);
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
				Marshal.FreeHGlobal (data);
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
				Marshal.FreeHGlobal (data);
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
				Marshal.FreeHGlobal (data);
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
				Marshal.FreeHGlobal (data);
			}
		}

		public TargetAddress ReadAddress (TargetAddress address)
		{
			check_disposed ();
			switch (TargetAddressSize) {
			case 4:
				return new TargetAddress (AddressDomain, (uint) ReadInteger (address));

			case 8:
				return new TargetAddress (AddressDomain, ReadLongInteger (address));

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
				return new TargetAddress (GlobalAddressDomain, (uint) ReadInteger (address));

			case 8:
				return new TargetAddress (GlobalAddressDomain, ReadLongInteger (address));

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

		public void WriteBuffer (TargetAddress address, byte[] buffer)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				int size = buffer.Length;
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
		public event DebuggerOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;
		public event StateChangedHandler StateChanged;

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				check_disposed ();
				return target_state;
			}
		}

		protected TargetState change_target_state (TargetState new_state)
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
			if (!disposed)
				mono_debugger_server_kill (server_handle);
		}

		public TargetAddress CurrentFrame {
			get {
				long pc;
				check_disposed ();
				CommandError result = mono_debugger_server_get_pc (server_handle, out pc);
				if (result != CommandError.None)
					throw new NoStackException ();

				return new TargetAddress (GlobalAddressDomain, pc);
			}
		}

		public bool CurrentInstructionIsBreakpoint {
			get {
				check_disposed ();
				int is_breakpoint;
				CommandError result = mono_debugger_server_current_insn_is_bpt (
					server_handle, out is_breakpoint);
				if (result != CommandError.None)
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

		internal struct ServerStackFrame
		{
			public long Address;
			public long FrameAddress;
		}

		internal class StackFrame
		{
			Inferior inferior;
			ServerStackFrame frame;

			internal StackFrame (Inferior inferior, ServerStackFrame frame)
			{
				this.inferior = inferior;
				this.frame = frame;
			}

			public TargetAddress Address {
				get {
					return new TargetAddress (inferior.GlobalAddressDomain, frame.Address);
				}
			}
		}

		public StackFrame[] GetBacktrace (int max_frames, TargetAddress stop)
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

				StackFrame[] retval = new StackFrame [count];
				for (int i = 0; i < count; i++)
					retval [i] = new StackFrame (this, frames [i]);
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

			return new TargetAddress (GlobalAddressDomain, address);
		}

		public TargetAddress GetStackPointer ()
		{
			long esp = GetRegister ((int) I386Register.ESP);

			return new TargetAddress (address_domain, esp);
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
			string contents = Utils.GetFileContents (mapfile);

			if (contents == null)
				return null;

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
						new TargetAddress (GlobalAddressDomain, start),
						new TargetAddress (GlobalAddressDomain, end),
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

		public abstract int SIGKILL {
			get;
		}
		public abstract int SIGSTOP {
			get;
		}
		public abstract int SIGINT {
			get;
		}
		public abstract int SIGCHLD {
			get;
		}
		public abstract int SIGPROF {
			get;
		}
		public abstract int SIGPWR {
			get;
		}
		public abstract int SIGXCPU {
			get;
		}
		public abstract int ThreadAbortSignal {
			get;
		}
		public abstract int ThreadRestartSignal {
			get;
		}
		public abstract int ThreadDebugSignal {
			get;
		}
		public abstract int MonoThreadDebugSignal {
			get;
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
					Kill ();
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

		~Inferior ()
		{
			Dispose (false);
		}
	}
}
