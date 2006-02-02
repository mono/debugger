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

using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal delegate void ChildOutputHandler (string output);

	internal class Inferior : ITargetMemoryAccess, ITargetNotification, IDisposable
	{
		protected IntPtr server_handle;
		protected Bfd bfd;
		protected BfdDisassembler bfd_disassembler;
		protected ThreadManager thread_manager;

		protected readonly ProcessStart start;

		protected readonly BfdContainer bfd_container;
		protected readonly SymbolTableCollection symtab_collection;
		protected readonly Debugger backend;
		protected readonly DebuggerErrorHandler error_handler;
		protected readonly BreakpointManager breakpoint_manager;
		protected readonly AddressDomain address_domain;
		protected readonly bool native;

		protected ChildOutputHandler stdout_handler;
		protected ChildOutputHandler stderr_handler;

		int child_pid;
		long tid;
		bool initialized;

		TargetInfo target_info;
		TargetMemoryInfo target_memory_info;
		Architecture arch;

		bool has_signals;
		SignalInfo signal_info;

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

		public long TID {
			get {
				check_disposed ();
				return tid;
			}
		}

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_initialize (IntPtr handle, int child_pid, out long tid);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_spawn (IntPtr handle, string working_directory, string[] argv, string[] envp, out int child_pid, ChildOutputHandler stdout_handler, ChildOutputHandler stderr_handler, out IntPtr error);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_attach (IntPtr handle, int child_pid, bool is_main, out long tid);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_frame (IntPtr handle, out ServerStackFrame frame);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_current_insn_is_bpt (IntPtr handle, out int is_breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_step (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_continue (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_detach (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_finalize (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_read_memory (IntPtr handle, long start, int size, IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_write_memory (IntPtr handle, long start, int size, IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_target_info (out int target_int_size, out int target_long_size, out int target_address_size, out int is_bigendian);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method (IntPtr handle, long method_address, long method_argument1, long method_argument2, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_1 (IntPtr handle, long method_address, long method_argument, string string_argument, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_2 (IntPtr handle, long method_address, long method_argument, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_abort_invoke (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_invoke (IntPtr handle, long invoke_method, long method_address, int num_params, int blob_size, IntPtr param_data, IntPtr offset_data, IntPtr blob_data, long callback_argument, bool debug);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_insert_breakpoint (IntPtr handle, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_insert_hw_breakpoint (IntPtr handle, out int index, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_remove_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_enable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_disable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_registers (IntPtr handle, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_set_registers (IntPtr handle, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_stop (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_global_stop ();

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_stop_and_wait (IntPtr handle, out int status);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_set_signal (IntPtr handle, int signal, int send_it);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_kill (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_create_inferior (IntPtr breakpoint_manager);

		[DllImport("monodebuggerserver")]
		static extern ChildEventType mono_debugger_server_dispatch_event (IntPtr handle, int status, out long arg, out long data1, out long data2);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_signal_info (IntPtr handle, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_threads (IntPtr handle, out int count, out IntPtr data);

		internal enum ChildEventType {
			NONE = 0,
			UNKNOWN_ERROR = 1,
			CHILD_EXITED,
			CHILD_STOPPED,
			CHILD_SIGNALED,
			CHILD_CALLBACK,
			CHILD_CALLBACK_COMPLETED,
			CHILD_HIT_BREAKPOINT,
			CHILD_MEMORY_CHANGED,
			CHILD_CREATED_THREAD,
			CHILD_NOTIFICATION,
			UNHANDLED_EXCEPTION,
			THROW_EXCEPTION,
			HANDLE_EXCEPTION
		}

		internal delegate void ChildEventHandler (ChildEventType message, int arg);

		internal sealed class ChildEvent
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

		[StructLayout(LayoutKind.Sequential)]
		private struct SignalInfo
		{
			public int SIGKILL;
			public int SIGSTOP;
			public int SIGINT;
			public int SIGCHLD;

			public int MonoThreadAbortSignal;

			public override string ToString ()
			{
				return String.Format ("SignalInfo ({0}:{1}:{2}:{3} - {4})",
						      SIGKILL, SIGSTOP, SIGINT, SIGCHLD,
						      MonoThreadAbortSignal);
			}
		}

		protected Inferior (ThreadManager thread_manager, Debugger backend,
				    ProcessStart start, BreakpointManager bpm,
				    DebuggerErrorHandler error_handler,
				    AddressDomain address_domain)
		{
			this.thread_manager = thread_manager;
			this.backend = backend;
			this.start = start;
			this.native = start.IsNative;
			this.bfd_container = backend.BfdContainer;
			this.error_handler = error_handler;
			this.breakpoint_manager = bpm;
			this.address_domain = address_domain;

			server_handle = mono_debugger_server_create_inferior (breakpoint_manager.Manager);
			if (server_handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");
		}

		public static Inferior CreateInferior (ThreadManager thread_manager,
						       ProcessStart start)
		{
			return new Inferior (
				thread_manager, thread_manager.Debugger, start,
				thread_manager.BreakpointManager, null,
				thread_manager.AddressDomain);
		}

		public Inferior CreateThread ()
		{
			Inferior inferior = new Inferior (
				thread_manager, backend, start, breakpoint_manager,
				error_handler, address_domain);

			inferior.signal_info = signal_info;
			inferior.has_signals = has_signals;

			inferior.target_info = target_info;
			inferior.target_memory_info = target_memory_info;
			inferior.bfd = bfd;

			inferior.arch = inferior.bfd.Architecture;
			inferior.bfd_disassembler = inferior.bfd.GetDisassembler (inferior);

			return inferior;
		}

		[DllImport("libglib-2.0-0.dll")]
		protected extern static void g_free (IntPtr data);

		protected void check_error (TargetError error)
		{
			if (error == TargetError.None)
				return;

			throw new TargetException (error);
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

		public void CallMethod (TargetAddress method, long arg1, long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				check_error (mono_debugger_server_call_method_2 (
					server_handle, method.Address, arg1, callback_arg));
			} catch {
				change_target_state (old_state);
			}
		}

		public void RuntimeInvoke (TargetAccess target, TargetAddress invoke_method,
					   TargetAddress method_argument,
					   TargetObject object_argument, TargetObject[] param_objects,
					   long callback_arg, bool debug)
		{
			check_disposed ();

			int length = param_objects.Length + 1;

			TargetObject[] input_objects = new TargetObject [length];
			input_objects [0] = object_argument;
			param_objects.CopyTo (input_objects, 1);

			int blob_size = 0;
			byte[][] blobs = new byte [length][];
			int[] blob_offsets = new int [length];
			long[] addresses = new long [length];

			for (int i = 0; i < length; i++) {
				TargetObject obj = input_objects [i];

				if (obj == null)
					continue;
				if (obj.Location.HasAddress) {
					blob_offsets [i] = -1;
					addresses [i] = obj.Location.Address.Address;
					continue;
				}
				blobs [i] = obj.Location.ReadBuffer (target, obj.Type.Size);
				blob_offsets [i] = blob_size;
				blob_size += blobs [i].Length;
			}

			byte[] blob = new byte [blob_size];
			blob_size = 0;
			for (int i = 0; i < length; i++) {
				if (blobs [i] == null)
					continue;
				blobs [i].CopyTo (blob, blob_size);
				blob_size += blobs [i].Length;
			}

			IntPtr blob_data = IntPtr.Zero, param_data = IntPtr.Zero;
			IntPtr offset_data = IntPtr.Zero;
			try {
				if (blob_size > 0) {
					blob_data = Marshal.AllocHGlobal (blob_size);
					Marshal.Copy (blob, 0, blob_data, blob_size);
				}

				param_data = Marshal.AllocHGlobal (length * 8);
				Marshal.Copy (addresses, 0, param_data, length);

				offset_data = Marshal.AllocHGlobal (length * 4);
				Marshal.Copy (blob_offsets, 0, offset_data, length);

				check_error (mono_debugger_server_call_method_invoke (
					server_handle, invoke_method.Address, method_argument.Address,
					length, blob_size, param_data, offset_data, blob_data,
					callback_arg, debug));
			} finally {
				if (blob_data != IntPtr.Zero)
					Marshal.FreeHGlobal (blob_data);
				Marshal.FreeHGlobal (param_data);
				Marshal.FreeHGlobal (offset_data);
			}
		}

		public void AbortInvoke ()
		{
			check_error (mono_debugger_server_abort_invoke (server_handle));
		}

		public int InsertBreakpoint (TargetAddress address)
		{
			int retval;
			check_error (mono_debugger_server_insert_breakpoint (
				server_handle, address.Address, out retval));
			return retval;
		}

		public int InsertHardwareBreakpoint (TargetAddress address, bool fallback,
						     out int index)
		{
			int retval;
			TargetError result = mono_debugger_server_insert_hw_breakpoint (
				server_handle, out index, address.Address, out retval);
			if (result == TargetError.None)
				return retval;
			else if (fallback &&
				 ((result == TargetError.DebugRegisterOccupied) ||
				  (result == TargetError.NotImplemented))) {
				index = -1;
				return InsertBreakpoint (address);
			} else {
				throw new TargetException (result);
			}
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

		public Debugger Debugger {
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
				throw new TargetException (TargetError.AlreadyHaveTarget);

			initialized = true;

			IntPtr error;

			stdout_handler = new ChildOutputHandler (inferior_stdout_handler);
			stderr_handler = new ChildOutputHandler (inferior_stderr_handler);

			TargetError result = mono_debugger_server_spawn (
				server_handle, start.WorkingDirectory, start.CommandLineArguments,
				start.Environment, out child_pid,
				stdout_handler,
				stderr_handler,
				out error);
			if (result != TargetError.None) {
				string message = Marshal.PtrToStringAuto (error);
				g_free (error);

				throw new TargetException (
					TargetError.CannotStartTarget, message);
			}

			SetupInferior ();

			change_target_state (TargetState.STOPPED, 0);
		}

		public void Initialize (int pid)
		{
			if (initialized)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			initialized = true;

			check_error (mono_debugger_server_initialize (server_handle, pid, out tid));
			this.child_pid = pid;

			SetupInferior ();

			change_target_state (TargetState.STOPPED, 0);
		}

		public void Attach (int pid, bool is_main)
		{
			if (initialized)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			initialized = true;

			check_error (mono_debugger_server_attach (server_handle, pid, is_main, out tid));
			this.child_pid = pid;

			SetupInferior ();

			change_target_state (TargetState.STOPPED, 0);
		}

		public CoreFile OpenCoreFile (string core_file)
		{
			SetupInferior ();

			CoreFile core = new CoreFile (thread_manager, this, bfd, core_file);
			return core;
		}

		public ChildEvent ProcessEvent (int status)
		{
			long arg, data1, data2;
			ChildEventType message;

			message = mono_debugger_server_dispatch_event (
				server_handle, status, out arg, out data1, out data2);

			switch (message) {
			case ChildEventType.CHILD_EXITED:
			case ChildEventType.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED);
				break;

			case ChildEventType.CHILD_CALLBACK:
			case ChildEventType.CHILD_CALLBACK_COMPLETED:
			case ChildEventType.CHILD_STOPPED:
			case ChildEventType.CHILD_HIT_BREAKPOINT:
				change_target_state (TargetState.STOPPED);
				break;
			}

			return new ChildEvent (message, arg, data1, data2);
		}

		protected void SetupInferior ()
		{
			IntPtr data = IntPtr.Zero;
			try {
				check_error (mono_debugger_server_get_signal_info (
						     server_handle, out data));

				signal_info = (SignalInfo) Marshal.PtrToStructure (
					data, typeof (SignalInfo));
				has_signals = true;
			} finally {
				g_free (data);
			}

			int target_int_size, target_long_size, target_addr_size, is_bigendian;
			check_error (mono_debugger_server_get_target_info
				(out target_int_size, out target_long_size,
				 out target_addr_size, out is_bigendian));

			target_info = new TargetInfo (target_int_size, target_long_size,
						      target_addr_size, is_bigendian != 0);
			target_memory_info = new TargetMemoryInfo (target_int_size, target_long_size,
								   target_addr_size, is_bigendian != 0,
								   address_domain);

			try {
				bfd = bfd_container.AddFile (
					this, start.TargetApplication, TargetAddress.Null,
					start.LoadNativeSymbolTable, true);
			} catch (Exception e) {
				if (error_handler != null)
					error_handler (this, String.Format (
							       "Can't read symbol file {0}", start.TargetApplication), e);
				else
					Console.WriteLine ("Can't read symbol file {0}: {1}",
							   start.TargetApplication, e);
				return;
			}

			bfd_container.SetupInferior (this, bfd);

			arch = bfd.Architecture;
			target_memory_info.Initialize (arch);

			bfd_disassembler = bfd.GetDisassembler (this);
		}

		public void InitializeModules ()
		{
			bfd.UpdateSharedLibraryInfo (this, this);
		}

		public BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
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
				return bfd.EntryPoint;
			}
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

		public bool IsBigEndian {
			get {
				return target_info.IsBigEndian;
			}
		}

		//
		// ITargetMemoryAccess
		//

		public AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}

		public ITargetInfo TargetInfo {
			get {
				return target_info;
			}
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get {
				return target_memory_info;
			}
		}

		IntPtr read_buffer (TargetAddress address, int size)
		{
			IntPtr data = Marshal.AllocHGlobal (size);
			TargetError result = mono_debugger_server_read_memory (
				server_handle, address.Address, size, data);
			if (result == TargetError.MemoryAccess) {
				Marshal.FreeHGlobal (data);
				throw new TargetMemoryException (address, size);
			} else if (result != TargetError.None) {
				Marshal.FreeHGlobal (data);
				throw new TargetException (result);
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
			TargetAddress res;
			switch (TargetAddressSize) {
			case 4:
				res = new TargetAddress (AddressDomain, (uint) ReadInteger (address));
				break;

			case 8:
				res = new TargetAddress (AddressDomain, ReadLongInteger (address));
				break;

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
			}

			if (res.Address == 0)
				return TargetAddress.Null;
			else
				return res;
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

		public TargetBlob ReadMemory (TargetAddress address, int size)
		{
			check_disposed ();
			byte [] retval = ReadBuffer (address, size);
			return new TargetBlob (retval, target_info);
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
					server_handle, address.Address, size, data));
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
					server_handle, address.Address, 1, data));
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
					server_handle, address.Address, 4, data));
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
					server_handle, address.Address, 8, data));
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

		public int InsertBreakpoint (Breakpoint bpt, TargetAddress address)
		{
			return breakpoint_manager.InsertBreakpoint (this, bpt, address);
		}

		//
		// IInferior
		//

		public event TargetOutputHandler TargetOutput;
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

		// <summary>
		//   Stop the inferior.
		//   Returns true if it actually stopped the inferior and false if it was
		//   already stopped.
		//   Note that the target may have stopped abnormally in the meantime, in
		//   this case we return the corresponding ChildEvent.
		// </summary>
		public bool Stop (out ChildEvent new_event)
		{
			check_disposed ();
			int status;
			TargetError error = mono_debugger_server_stop_and_wait (server_handle, out status);
			if (error != TargetError.None) {
				new_event = null;
				return false;
			} else if (status == 0) {
				new_event = null;
				return true;
			}

			new_event = ProcessEvent (status);
			return true;
		}

		// <summary>
		//   Just send the inferior a stop signal, but don't wait for it to stop.
		//   Returns true if it actually sent the signal and false if the target
		//   was already stopped.
		// </summary>
		public bool Stop ()
		{
			check_disposed ();
			TargetError error = mono_debugger_server_stop (server_handle);
			return error == TargetError.None;
		}

		public void GlobalStop ()
		{
			mono_debugger_server_global_stop ();
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
				ServerStackFrame frame = get_current_frame ();
				return new TargetAddress (AddressDomain, frame.Address);
			}
		}

		public bool CurrentInstructionIsBreakpoint {
			get {
				check_disposed ();
				int is_breakpoint;
				TargetError result = mono_debugger_server_current_insn_is_bpt (
					server_handle, out is_breakpoint);
				if (result != TargetError.None)
					throw new TargetException (TargetError.NoStack);

				return is_breakpoint != 0;
			}
		}

		public Disassembler Disassembler {
			get {
				check_disposed ();
				return bfd_disassembler;
			}
		}

		public Architecture Architecture {
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

		public Registers GetRegisters ()
		{
			IntPtr buffer = IntPtr.Zero;
			try {
				int count = arch.CountRegisters;
				int buffer_size = count * 8;
				buffer = Marshal.AllocHGlobal (buffer_size);
				TargetError result = mono_debugger_server_get_registers (
					server_handle, buffer);
				check_error (result);
				long[] retval = new long [count];
				Marshal.Copy (buffer, retval, 0, count);

				return new Registers (arch, retval);
			} finally {
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public void SetRegisters (Registers registers)
		{
			IntPtr buffer = IntPtr.Zero;
			try {
				int count = arch.CountRegisters;
				int buffer_size = count * 8;
				buffer = Marshal.AllocHGlobal (buffer_size);
				Marshal.Copy (registers.Values, 0, buffer, count);
				TargetError result = mono_debugger_server_set_registers (
					server_handle, buffer);
				check_error (result);
			} finally {
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public int[] GetThreads ()
		{
			IntPtr data = IntPtr.Zero;
			try {
				int count;
				check_error (mono_debugger_server_get_threads (
						     server_handle, out count, out data));

				int[] threads = new int [count];
				Marshal.Copy (data, threads, 0, count);
				return threads;
			} finally {
				g_free (data);
			}
		}

		internal struct ServerStackFrame
		{
			public long Address;
			public long StackPointer;
			public long FrameAddress;
		}

		internal class StackFrame
		{
			TargetAddress address, stack, frame;

			internal StackFrame (ITargetMemoryInfo info, ServerStackFrame frame)
			{
				this.address = new TargetAddress (info.AddressDomain, frame.Address);
				this.stack = new TargetAddress (info.AddressDomain, frame.StackPointer);
				this.frame = new TargetAddress (info.AddressDomain, frame.FrameAddress);
			}

			internal StackFrame (TargetAddress address, TargetAddress stack,
					     TargetAddress frame)
			{
				this.address = address;
				this.stack = stack;
				this.frame = frame;
			}

			public TargetAddress Address {
				get {
					return address;
				}
			}

			public TargetAddress StackPointer {
				get {
					return stack;
				}
			}

			public TargetAddress FrameAddress {
				get {
					return frame;
				}
			}
		}

		ServerStackFrame get_current_frame ()
		{
			check_disposed ();
			ServerStackFrame frame;
			TargetError result = mono_debugger_server_get_frame (
				server_handle, out frame);
			if (result != TargetError.None)
				throw new TargetException (TargetError.NoStack);
			return frame;
		}

		public StackFrame GetCurrentFrame ()
		{
			ServerStackFrame frame = get_current_frame ();
			return new StackFrame (this, frame);
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
						new TargetAddress (AddressDomain, start),
						new TargetAddress (AddressDomain, end),
						flags, name);
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

		public int SIGKILL {
			get {
				if (!has_signals || (signal_info.SIGKILL < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGKILL;
			}
		}

		public int SIGSTOP {
			get {
				if (!has_signals || (signal_info.SIGSTOP < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGSTOP;
			}
		}

		public int SIGINT {
			get {
				if (!has_signals || (signal_info.SIGINT < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGINT;
			}
		}

		public int SIGCHLD {
			get {
				if (!has_signals || (signal_info.SIGCHLD < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGCHLD;
			}
		}

		public int MonoThreadAbortSignal {
			get {
				if (!has_signals || (signal_info.MonoThreadAbortSignal < 0))
					throw new InvalidOperationException ();

				return signal_info.MonoThreadAbortSignal;
			}
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
					if (bfd_disassembler != null)
						bfd_disassembler.Dispose ();
				}
				
				this.disposed = true;

				// Release unmanaged resources
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
