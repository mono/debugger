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
// <summary>
//   The single stepping engine is responsible for doing all the stepping
//   operations.
//
//     sse                  - short for single stepping engine.
//
//     stepping operation   - an operation which has been invoked by the user such
//                            as StepLine(), NextLine() etc.
//
//     atomic operation     - an operation which the sse invokes on the target
//                            such as stepping one machine instruction or resuming
//                            the target until a breakpoint is hit.
//
//     step frame           - an address range; the sse invokes atomic operations
//                            until the target hit a breakpoint, received a signal
//                            or stopped at an address outside this range.
//
//     temporary breakpoint - a breakpoint which is automatically removed the next
//                            time the target stopped; it is used to step over
//                            method calls.
//
//     source stepping op   - stepping operation based on the program's source code,
//                            such as StepLine() or NextLine().
//
//     native stepping op   - stepping operation based on the machine code such as
//                            StepInstruction() or NextInstruction().
//
//   The SingleSteppingEngine supports both synchronous and asynchronous
//   operations; in synchronous mode, the engine waits until the child has stopped
//   before returning.  In either case, the step commands return true on success
//   and false an error.
//
//   Since the SingleSteppingEngine can be used from multiple threads at the same
//   time, you can no longer safely use the `State' property to find out whether
//   the target is stopped or not.  It is safe to call all the step commands from
//   multiple threads, but for obvious reasons only one command can run at a
//   time.  So if you attempt to issue a step command while the engine is still
//   busy, the step command will return false to signal this error.
// </summary>

	// <summary>
	//   The ThreadManager creates one SingleSteppingEngine instance for each thread
	//   in the target.
	//
	//   The `SingleSteppingEngine' class is basically just responsible for whatever happens
	//   in the background thread: processing commands and events.  Their methods
	//   are just meant to be called from the SingleSteppingEngine (since it's a
	//   protected nested class they can't actually be called from anywhere else).
	//
	//   See the `Process' class for the "user interface".
	// </summary>
	internal class SingleSteppingEngine : ITargetMemoryInfo
	{
		protected SingleSteppingEngine (ThreadManager manager, Inferior inferior)
		{
			this.manager = manager;
			this.inferior = inferior;
			this.start = inferior.ProcessStart;
			this.process = new Process (this, start, inferior);

			PID = inferior.PID;

			operation_completed_event = new DebuggerManualResetEvent (
				"operation_completed_event", false);
		}

		public SingleSteppingEngine (ThreadManager manager, ProcessStart start)
			: this (manager, Inferior.CreateInferior (manager, start))
		{
			inferior.Run (true);
			PID = inferior.PID;

			is_main = true;

			setup_engine ();
		}

		public SingleSteppingEngine (ThreadManager manager, Inferior inferior, int pid)
			: this (manager, inferior)
		{
			this.PID = pid;
			inferior.Attach (pid);

			is_main = false;
			TID = inferior.TID;

			setup_engine ();
		}

		void setup_engine ()
		{
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			Report.Debug (DebugFlags.Threads, "New SSE ({0}): {1}",
				      DebuggerWaitHandle.CurrentThread, this);

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			disassembler.SymbolTable = manager.SymbolTableManager.SimpleSymbolTable;
			current_simple_symtab = manager.SymbolTableManager.SimpleSymbolTable;
			current_symtab = manager.SymbolTableManager.SymbolTable;

			native_language = new Mono.Debugger.Languages.Native.NativeLanguage ((ITargetInfo) inferior);

			manager.SymbolTableManager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);
		}

		// <summary>
		//   This is called from the SingleSteppingEngine's main event loop to give
		//   us the next event - `status' has no meaning to us, it's just meant to
		//   be passed to inferior.ProcessEvent() to get the actual event.
		// </summary>
		// <remarks>
		//   Actually, `status' is the waitpid() status code.  In Linux 2.6.x, you
		//   can call waitpid() from any thread in the debugger, but we need to get
		//   the target's registers to find out whether it's a breakpoint etc.
		//   That's done in inferior.ProcessEvent() - which must always be called
		//   from the engine's thread.
		// </remarks>
		public void ProcessEvent (int status)
		{
			Inferior.ChildEvent cevent = inferior.ProcessEvent (status);
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION)
				Report.Debug (DebugFlags.Notification,
					      "{0} received event {1} ({2:x})",
					      this, cevent, status);
			else
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} ({2:x})",
					      this, cevent, status);

			if (stepping_over_breakpoint > 0) {
				Report.Debug (DebugFlags.SSE,
					      "{0} stepped over breakpoint {1}: {2}",
					      this, stepping_over_breakpoint,
					      inferior.CurrentFrame);

				inferior.EnableBreakpoint (stepping_over_breakpoint);
				manager.ReleaseGlobalThreadLock (this);
				stepping_over_breakpoint = 0;

				Report.Debug (DebugFlags.SSE,
					      "{0} stepped over breakpoint: {1} ({2:x}) {3}",
					      this, cevent, status, current_operation);
			}

			if (manager.HandleChildEvent (inferior, ref cevent))
				return;
			ProcessEvent (cevent);
		}

		void send_frame_event (StackFrame frame, int signal)
		{
			operation_completed (new TargetEventArgs (TargetEventType.TargetStopped, signal, frame));
		}

		void send_frame_event (StackFrame frame, BreakpointHandle handle)
		{
			operation_completed (new TargetEventArgs (TargetEventType.TargetHitBreakpoint, handle, frame));
		}

		void operation_completed (TargetEventArgs result)
		{
			lock (this) {
				engine_stopped = true;
				if (result != null)
					send_target_event (result);
				else
					target_state = TargetState.STOPPED;
				operation_completed_event.Set ();
			}
		}

		void send_target_event (TargetEventArgs args)
		{
			Report.Debug (DebugFlags.EventLoop, "{0} sending target event {1}",
				      this, args);

			switch (args.Type) {
			case TargetEventType.TargetRunning:
				target_state = TargetState.RUNNING;
				break;

			case TargetEventType.TargetSignaled:
			case TargetEventType.TargetExited:
				target_state = TargetState.EXITED;
				break;

			default:
				target_state = TargetState.STOPPED;
				break;
			}

			process.SendTargetEvent (args);
		}

		internal void Start (TargetAddress func, bool is_main)
		{
			if (is_main) {
				if (!func.IsNull)
					insert_temporary_breakpoint (func);
				current_operation = new Operation (OperationType.Initialize);
				this.is_main = is_main;
			} else {
				current_operation = new Operation (OperationType.RunInBackground);
			}
			do_continue ();
		}

		// <summary>
		//   Process a synchronous command.
		// </summary>
		public CommandResult ProcessCommand (Command command)
		{
			object result = do_process_command (command.Type, command.Data1, command.Data2);

			return new CommandResult (CommandResultType.CommandOk, result);
		}

		object do_process_command (CommandType type, object data, object data2)
		{
			switch (type) {
			case CommandType.GetBacktrace:
				return get_backtrace ((int) data);

			case CommandType.GetRegisters:
				return get_registers ();

			case CommandType.SetRegisters:
				set_registers ((Registers) data);
				break;

			case CommandType.InsertBreakpoint:
				return manager.BreakpointManager.InsertBreakpoint (
					inferior, (Breakpoint) data, (TargetAddress) data2);

			case CommandType.RemoveBreakpoint:
				manager.BreakpointManager.RemoveBreakpoint (
					inferior, (int) data);
				break;

			case CommandType.GetInstructionSize:
				return get_insn_size ((TargetAddress) data);

			case CommandType.DisassembleInstruction:
				return disassemble_insn ((IMethod) data, (TargetAddress) data2);

			case CommandType.DisassembleMethod:
				return disassemble_method ((IMethod) data);

			case CommandType.ReadMemory:
				return do_read_memory ((TargetAddress) data, (int) data2);

			case CommandType.ReadString:
				return do_read_string ((TargetAddress) data);

			case CommandType.WriteMemory:
				do_write_memory ((TargetAddress) data, (byte []) data2);
				break;

			default:
				throw new InternalError ();
			}

			return null;
		}

		// <summary>
		//   Start a new stepping operation.
		//
		//   All stepping operations are done asynchronously.
		//
		//   The inferior basically just knows two kinds of stepping operations:
		//   there is do_continue() to continue execution (until a breakpoint is
		//   hit or the target receives a signal or exits) and there is do_step()
		//   to single-step one machine instruction.  There's also a version of
		//   do_continue() which takes an address - it inserts a temporary breakpoint
		//   on that address and calls do_continue().
		//
		//   Let's call these "atomic operations" while a "stepping operation" is
		//   something like stepping until the next source line.  We normally need to
		//   do several atomic operations for each stepping operation.
		//
		//   We start a new stepping operation here, but what we actually do is
		//   starting an atomic operation on the target.  Note that we just start it,
		//   but don't wait until is completed.  Once the target is running, we go
		//   back to the main event loop and wait for it (or another thread) to stop
		//   (or to get another command from the user).
		// </summary>
		public void ProcessCommand (Operation operation)
		{
			stop_requested = false;

			// Process another stepping command.
			switch (operation.Type) {
			case OperationType.Run:
			case OperationType.RunInBackground:
				Step (operation);
				break;

			case OperationType.StepNativeInstruction:
				do_step ();
				break;

			case OperationType.NextInstruction:
				do_next ();
				break;

			case OperationType.RuntimeInvoke:
				do_callback (new CallbackRuntimeInvoke (
						     operation.RuntimeInvokeData));
				break;

			case OperationType.CallMethod:
				do_callback (new CallbackCallMethod (
						     operation.CallMethodData));
				break;

			case OperationType.StepLine:
				operation.StepFrame = get_step_frame ();
				if (operation.StepFrame == null)
					do_step ();
				else
					Step (operation);
				break;

			case OperationType.NextLine:
				// We cannot just set a breakpoint on the next line
				// since we do not know which way the program's
				// control flow will go; ie. there may be a jump
				// instruction before reaching the next line.
				StepFrame frame = get_step_frame ();
				if (frame == null)
					do_next ();
				else {
					operation.StepFrame = new StepFrame (
						frame.Start, frame.End, null, StepMode.Finish);
					Step (operation);
				}
				break;

			case OperationType.StepInstruction:
				operation.StepFrame = get_step_frame (StepMode.SingleInstruction);
				Step (operation);
				break;

			case OperationType.StepFrame:
				Step (operation);
				break;

			case OperationType.FinishNative:
				Step (operation);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		// <summary>
		//   Process one event from the target.  The return value specifies whether
		//   we started another atomic operation or whether the target is still
		//   stopped.
		//
		//   This method is called each time an atomic operation is completed - or
		//   something unexpected happened, for instance we hit a breakpoint, received
		//   a signal or just died.
		//
		//   Now, our first task is figuring out whether the atomic operation actually
		//   completed, ie. the target stopped normally.
		// </summary>
		protected void ProcessEvent (Inferior.ChildEvent cevent)
		{
			Inferior.ChildEventType message = cevent.Type;
			int arg = (int) cevent.Argument;

			// Callbacks happen when the user (or the engine) called a method
			// in the target (RuntimeInvoke).
			if (current_callback != null) {
				if (!current_callback.ProcessEvent (this, inferior, cevent))
					return;

				current_callback = null;

				if (message == Inferior.ChildEventType.CHILD_CALLBACK) {
					Report.Debug (DebugFlags.EventLoop,
						      "{0} completed callback", this);

					// Ok, inform the user that we stopped.
					TargetEventArgs args = new TargetEventArgs (
						TargetEventType.FrameChanged, current_frame);
					step_operation_finished ();
					operation_completed (args);
					return;
				}
			}

			TargetEventArgs result = null;

			// To step over a method call, the sse inserts a temporary
			// breakpoint immediately after the call instruction and then
			// resumes the target.
			//
			// If the target stops and we have such a temporary breakpoint, we
			// need to distinguish a few cases:
			//
			// a) we may have received a signal
			// b) we may have hit another breakpoint
			// c) we actually hit the temporary breakpoint
			//
			// In either case, we need to remove the temporary breakpoint if
			// the target is to remain stopped.  Note that this piece of code
			// here only deals with the temporary breakpoint, the handling of
			// a signal or another breakpoint is done later.
			if (temp_breakpoint_id != 0) {
				if ((message == Inferior.ChildEventType.CHILD_EXITED) ||
				    (message == Inferior.ChildEventType.CHILD_SIGNALED))
					// we can't remove the breakpoint anymore after
					// the target exited, but we need to clear this id.
					temp_breakpoint_id = 0;
				else if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
					if (arg == temp_breakpoint_id) {
						// we hit the temporary breakpoint; this'll always
						// happen in the `correct' thread since the
						// `temp_breakpoint_id' is only set in this
						// SingleSteppingEngine and not in any other thread's.
						message = Inferior.ChildEventType.CHILD_STOPPED;
						arg = 0;

						inferior.RemoveBreakpoint (temp_breakpoint_id);
						temp_breakpoint_id = 0;
					}
				}
			}

			if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
				// Ok, the next thing we need to check is whether this is actually "our"
				// breakpoint or whether it belongs to another thread.  In this case,
				// `step_over_breakpoint' does everything for us and we can just continue
				// execution.
				if (stop_requested) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
					result = new TargetEventArgs (TargetEventType.TargetHitBreakpoint, arg, current_frame);
				} else if (arg == 0) {
					// Unknown breakpoint, always stop.
				} else if (step_over_breakpoint (TargetAddress.Null, false)) {
					return;
				} else if (!child_breakpoint (arg)) {
					// we hit any breakpoint, but its handler told us
					// to resume the target and continue.
					do_continue ();
					return;
				}

				step_operation_finished ();
			}

			if (temp_breakpoint_id != 0) {
				Report.Debug (DebugFlags.SSE,
					      "{0} hit temporary breakpoint at {1}",
					      this, inferior.CurrentFrame);

				if (!stop_requested &&
				    (message != Inferior.ChildEventType.UNHANDLED_EXCEPTION)) {
					inferior.Continue (); // do_continue ();
					return;
				}

				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}

			bool exiting = false;

			switch (message) {
			case Inferior.ChildEventType.CHILD_STOPPED:
				if (stop_requested || (arg != 0)) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
					result = new TargetEventArgs (
						TargetEventType.TargetStopped, arg,
						current_frame);
				}

				break;

			case Inferior.ChildEventType.UNHANDLED_EXCEPTION: {
				TargetAddress exc = new TargetAddress (
					manager.AddressDomain, cevent.Data1);
				TargetAddress ip = new TargetAddress (
					manager.AddressDomain, cevent.Data2);

				current_operation = new Operation (
					OperationType.Exception, exc);

				do_continue (ip);
				return;
			}

			case Inferior.ChildEventType.CHILD_HIT_BREAKPOINT:
				break;

			case Inferior.ChildEventType.CHILD_SIGNALED:
				result = new TargetEventArgs (TargetEventType.TargetSignaled, arg);
				exiting = true;
				break;

			case Inferior.ChildEventType.CHILD_EXITED:
				result = new TargetEventArgs (TargetEventType.TargetExited, arg);
				exiting = true;	
				break;

			case Inferior.ChildEventType.CHILD_CALLBACK:
				frame_changed (inferior.CurrentFrame, null);
				result = new TargetEventArgs (TargetEventType.TargetStopped, 0, current_frame);
				break;
			}

		send_result:
			// If `result' is not null, then the target stopped abnormally.
			if (result != null) {
				if (DaemonEventHandler != null) {
					// The `DaemonEventHandler' may decide to discard
					// this event in which case it returns true.
					if (DaemonEventHandler (this, inferior, result))
						return;
				}
				// Ok, inform the user that we stopped.
				step_operation_finished ();
				operation_completed (result);
				if (is_main && !reached_main && !exiting) {
					arch = inferior.Architecture;
					reached_main = true;
					main_method_retaddr = inferior.GetReturnAddress ();
					manager.ReachedMain ();
				}
				return;
			}

			//
			// Sometimes, we need to do just one atomic operation - in all
			// other cases, `current_operation' is the current stepping
			// operation.
			//
			// DoStep() will either start another atomic operation (and
			// return false) or tell us the stepping operation is completed
			// by returning true.
			//

			if (current_operation != null) {
				if (current_operation.Type == OperationType.Initialize) {
					Report.Debug (DebugFlags.SSE,
						      "{0} initialize ({1})", this,
						      DebuggerWaitHandle.CurrentThread);
					if (is_main)
						manager.Initialize (inferior);
					Report.Debug (DebugFlags.SSE, "{0} initialize done",
						      this);
				} else if (current_operation.Type == OperationType.Exception) {
					frame_changed (inferior.CurrentFrame, null);
					result = new TargetEventArgs (
						TargetEventType.UnhandledException,
						current_operation.Until,
						current_frame);
					goto send_result;
				} else if (!DoStep (false))
					return;
			}

			//
			// Ok, the target stopped normally.  Now we need to compute the
			// new stack frame and then send the result to our caller.
			//
			TargetAddress frame = inferior.CurrentFrame;

			// After returning from `main', resume the target and keep
			// running until it exits (or hit a breakpoint or receives
			// a signal).
			if (!main_method_retaddr.IsNull && (frame == main_method_retaddr)) {
				do_continue ();
				return;
			}

			//
			// We're done with our stepping operation, but first we need to
			// compute the new StackFrame.  While doing this, `frame_changed'
			// may discover that we need to do another stepping operation
			// before telling the user that we're finished.  This is to avoid
			// that we stop in things like a method's prologue or epilogue
			// code.  If that happens, we just continue stepping until we reach
			// the first actual source line in the method.
			//
			Operation new_operation = frame_changed (frame, current_operation);
			if (new_operation != null) {
				ProcessCommand (new_operation);
				return;
			}

			//
			// Now we're really finished.
			//
			step_operation_finished ();
			result = new TargetEventArgs (TargetEventType.TargetStopped, 0, current_frame);
			goto send_result;
		}

		CommandResult reload_symtab (object data)
		{
			current_frame = null;
			current_backtrace = null;
			registers = null;
			current_method = null;
			frame_changed (inferior.CurrentFrame, null);
			return CommandResult.Ok;
		}

		void update_symtabs (object sender, ISymbolTable symbol_table,
				     ISimpleSymbolTable simple_symtab)
		{
			disassembler.SymbolTable = simple_symtab;
			current_simple_symtab = simple_symtab;
			current_symtab = symbol_table;
		}

		public IMethod Lookup (TargetAddress address)
		{
			if (current_symtab == null)
				return null;

			return current_symtab.Lookup (address);
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (current_simple_symtab == null)
				return null;

			return current_simple_symtab.SimpleLookup (address, exact_match);
		}

		protected IMethod do_lookup (TargetAddress address)
		{
			manager.DebuggerBackend.UpdateSymbolTable ();
			return Lookup (address);
		}

		Registers get_registers ()
		{
			registers = inferior.GetRegisters ();
			return registers;
		}

		Backtrace get_backtrace (int max_frames)
		{
			manager.DebuggerBackend.UpdateSymbolTable ();

			if (current_frame == null)
				throw new TargetException (TargetError.NoStack);

			current_backtrace = new Backtrace (
				process, arch, current_frame, main_method_retaddr, max_frames);

			current_backtrace.GetBacktrace (
				inferior, arch, current_symtab, current_simple_symtab);

			return current_backtrace;
		}

		void set_registers (Registers registers)
		{
			if (!registers.FromCurrentFrame)
				throw new InvalidOperationException ();

			this.registers = registers;
			inferior.SetRegisters (registers);
		}

		int get_insn_size (TargetAddress address)
		{
			lock (disassembler) {
				return disassembler.GetInstructionSize (address);
			}
		}

		AssemblerLine disassemble_insn (IMethod method, TargetAddress address)
		{
			lock (disassembler) {
				return disassembler.DisassembleInstruction (method, address);
			}
		}

		AssemblerMethod disassemble_method (IMethod method)
		{
			lock (disassembler) {
				return disassembler.DisassembleMethod (method);
			}
		}

		public ISimpleSymbolTable SimpleSymbolTable {
			get {
				check_inferior ();
				lock (disassembler) {
					return disassembler.SymbolTable;
				}
			}

			set {
				check_inferior ();
				lock (disassembler) {
					disassembler.SymbolTable = value;
				}
			}
		}

		public ISymbolTable SymbolTable {
			get {
				check_inferior ();
				return current_symtab;
			}
		}

		byte[] do_read_memory (TargetAddress address, int size)
		{
			return inferior.ReadBuffer (address, size);
		}

		string do_read_string (TargetAddress address)
		{
			return inferior.ReadString (address);
		}

		void do_write_memory (TargetAddress address, byte[] data)
		{
			inferior.WriteBuffer (address, data);
		}

		//
		// ITargetInfo
		//

		public int TargetAddressSize {
			get {
				check_inferior ();
				return inferior.TargetAddressSize;
			}
		}

		public int TargetIntegerSize {
			get {
				check_inferior ();
				return inferior.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				check_inferior ();
				return inferior.TargetLongIntegerSize;
			}
		}

		public bool IsBigEndian {
			get {
				check_inferior ();
				return inferior.IsBigEndian;
			}
		}

		//
		// ITargetMemoryInfo
		//

		public AddressDomain AddressDomain {
			get {
				check_inferior ();
				return inferior.AddressDomain;
			}
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return manager.AddressDomain;
			}
		}

		public Process Process {
			get { return process; }
		}

		public ThreadManager ThreadManager {
			get { return manager; }
		}

		ThreadManager manager;
		Process process;
		Inferior inferior;
		IArchitecture arch;
		IDisassembler disassembler;
		ILanguage native_language;
		ProcessStart start;
		ISymbolTable current_symtab;
		ISimpleSymbolTable current_simple_symtab;
		bool engine_stopped;
		DebuggerManualResetEvent operation_completed_event;
		bool stop_requested;
		bool is_main, reached_main;
		bool native;
		public readonly int PID;
		public readonly int TID;

		int stepping_over_breakpoint;

		internal DaemonEventHandler DaemonEventHandler;
		internal bool IsDaemon;
		internal TargetAddress EndStackAddress;

		TargetAddress main_method_retaddr = TargetAddress.Null;
		TargetState target_state = TargetState.NO_TARGET;

		public ILanguage NativeLanguage {
			get {
				return native_language;
			}
		}

		public TargetState State {
			get {
				lock (this) {
					if (IsDaemon)
						return TargetState.DAEMON;
					else
						return target_state;
				}
			}
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_inferior ();
			return inferior.GetMemoryMaps ();
		}

		//
		// We support two kinds of commands:
		//
		// * synchronous commands are used for things like getting a backtrace
		//   or accessing the target's memory.
		//
		//   If you send such a command to the engine, its main event loop is
		//   blocked until the command finished, so it can send us the result
		//   back.
		//
		//   The background thread may always send synchronous commands (for
		//   instance from an event handler), so we do not acquire the
		//   `command_mutex'.  However, we must still make sure we aren't
		//   currently performing any async operation and ensure that no other
		//   thread can start such an operation while we're running the command.
		//   Because of this, we just acquire the `this' lock and then check
		//   whether `engine_stopped' is true.
		//
		// * asynchronous commands are used for all stepping operations and they
		//   can be either blocking (waiting for the operation to finish) or
		//   non-blocking.
		//
		//   In either case, we need to acquire the `command_mutex' before sending
		//   such a command and set `engine_stopped' to false (after checking that
		//   it was previously true) to ensure that nobody can send us a synchronous
		//   command.
		//
		//   `operation_completed_event' is reset by us and set when the operation
		//   finished.
		//
		//   In non-blocking mode, we start the command and then release the
		//   `command_mutex'.  Note that we can't just keep the mutex since it's
		//   "global": it protects the main event loop and thus blocks operations
		//   on all of the target's threads, not just on us.
		//
		// To summarize:
		//
		// * having the 'command_mutex' means that nobody can perform any operation
		//   on any of the target's threads, ie. we're "globally blocked".
		//
		// * if `engine_stopped' is false (you're only allowed to check if you own
		//   the `this' lock!), we're currently performing a stepping operation.
		//
		// * the `operation_completed_event' is used to wait until this stepping
		//   operation is finished.
		//


		// <summary>
		//   This must be called before sending any commands to the engine.
		//   It'll acquire the `command_mutex' and make sure that we aren't
		//   currently performing any async operation.
		//   Returns true on success and false if we're still busy.
		// </summary>
		// <remarks>
		//   If this returns true, you must call either AbortOperation() or
		//   SendAsyncCommand().
		// </remarks>
		public bool StartOperation ()
		{
			lock (this) {
				// First try to get the `command_mutex'.
				// If we succeed, then no synchronous command is currently
				// running.
				if (!manager.AcquireCommandMutex (this)) {
					Report.Debug (DebugFlags.Wait,
						      "{0} cannot get command mutex", this);
					return false;
				}
				// Check whether we're curring performing an async
				// stepping operation.
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					manager.ReleaseCommandMutex ();
					return false;
				}
				// This will never block.  The only thing which can
				// happen here is that we were running an async operation
				// and did not wait for the event yet.
				operation_completed_event.Wait ();
				engine_stopped = false;
				Report.Debug (DebugFlags.Wait,
					      "{0} got command mutex", this);
				return true;
			}
		}

		// <summary>
		//   Use this if you previously called StartOperation() and you changed
		//   your mind and don't want to start an operation anymore.
		// </summary>
		public void AbortOperation ()
		{
			lock (this) {
				Report.Debug (DebugFlags.Wait,
					      "{0} aborted operation", this);
				engine_stopped = true;
				manager.ReleaseCommandMutex ();
			}
		}

		// <summary>
		//   Sends a synchronous command to the engine.
		// </summary>
		public CommandResult SendSyncCommand (Command command)
		{
			lock (this) {
				// Check whether we're curring performing an async
				// stepping operation.
				if (!engine_stopped && !manager.InBackgroundThread) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					return CommandResult.Busy;
				}

				Report.Debug (DebugFlags.Wait,
					      "{0} sending sync command {1}",
					      this, command);
				CommandResult result = manager.SendSyncCommand (command);
				Report.Debug (DebugFlags.Wait,
					      "{0} finished sync command {1}",
					      this, command);

				return result;
			}
		}

		public CommandResult SendSyncCommand (CommandType type, object data1,
						      object data2)
		{
			return SendSyncCommand (new Command (this, type, data1, data2));
		}

		public CommandResult SendSyncCommand (CommandType type, object data)
		{
			return SendSyncCommand (new Command (this, type, data, null));
		}

		// <summary>
		//   Sends an async command to the engine.
		// </summary>
		// <remarks>
		//   You must call StartOperation() prior to calling this.
		// </remarks>	     
		public void SendAsyncCommand (Command command, bool wait)
		{
			lock (this) {
				operation_completed_event.Reset ();
				send_target_event (new TargetEventArgs (TargetEventType.TargetRunning, null));
				manager.SendAsyncCommand (command);
			}

			if (wait) {
				Report.Debug (DebugFlags.Wait, "{0} waiting", this);
				operation_completed_event.Wait ();
				Report.Debug (DebugFlags.Wait, "{0} done waiting", this);
			}
			Report.Debug (DebugFlags.Wait,
				      "{0} released command mutex", this);
			manager.ReleaseCommandMutex ();
		}

		public void SendCallbackCommand (Command command)
		{
			if (!StartOperation ())
				throw new TargetException (TargetError.NotStopped);

			lock (this) {
				operation_completed_event.Reset ();
				manager.SendAsyncCommand (command);
			}

			Report.Debug (DebugFlags.Wait, "{0} waiting", this);
			operation_completed_event.Wait ();
			Report.Debug (DebugFlags.Wait, "{0} done waiting", this);
			Report.Debug (DebugFlags.Wait,
				      "{0} released command mutex", this);
			manager.ReleaseCommandMutex ();
		}

		public bool Stop ()
		{
			return do_wait (true);
		}

		public bool Wait ()
		{
			return do_wait (false);
		}

		public void Kill ()
		{
			if (inferior != null)
				inferior.Kill ();
		}

		bool do_wait (bool stop)
		{
			lock (this) {
				// First try to get the `command_mutex'.
				// If we succeed, then no synchronous command is currently
				// running.
				if (!manager.AcquireCommandMutex (this)) {
					Report.Debug (DebugFlags.Wait,
						      "{0} cannot get command mutex", this);
					return false;
				}
				// Check whether we're curring performing an async
				// stepping operation.
				if (engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} already stopped", this);
					manager.ReleaseCommandMutex ();
					return true;
				}

				if (stop) {
					stop_requested = true;
					if (!inferior.Stop ()) {
						// We're already stopped, so just consider the
						// current operation as finished.
						step_operation_finished ();
						engine_stopped = true;
						stop_requested = false;
						operation_completed_event.Set ();
						manager.ReleaseCommandMutex ();
						return true;
					}
				}
			}

			// Ok, we got the `command_mutex'.
			// Now we can wait for the operation to finish.
			Report.Debug (DebugFlags.Wait, "{0} waiting", this);
			operation_completed_event.Wait ();
			Report.Debug (DebugFlags.Wait, "{0} stopped", this);
			manager.ReleaseCommandMutex ();
			return true;
		}

		public void Interrupt ()
		{
			lock (this) {
				Report.Debug (DebugFlags.EventLoop, "{0} interrupt: {1}",
					      this, engine_stopped);

				if (engine_stopped)
					return;

				stop_requested = true;
				bool stopped = inferior.Stop ();
				Report.Debug (DebugFlags.EventLoop, "{0} interrupt #1: {1}",
					      this, stopped);

				if (stepping_over_breakpoint > 0) {
					Report.Debug (DebugFlags.SSE,
						      "{0} aborting stepping over " +
						      "breakpoint {1}: {2}",
						      this, stepping_over_breakpoint,
						      inferior.CurrentFrame);

					inferior.EnableBreakpoint (stepping_over_breakpoint);
					manager.ReleaseGlobalThreadLock (this);
					stepping_over_breakpoint = 0;
				}

				if (current_callback != null) {
					current_callback.Abort ();
					current_callback = null;
				}

				if (!stopped) {
					// We're already stopped, so just consider the
					// current operation as finished.
					engine_stopped = true;
					stop_requested = false;

					frame_changed (inferior.CurrentFrame, null);
					TargetEventArgs args = new TargetEventArgs (
						TargetEventType.FrameChanged, current_frame);
					step_operation_finished ();
					operation_completed (args);
				}

				try {
					inferior.GetCurrentFrame ();
				} catch (Exception ex) {
					Console.WriteLine ("FUCK: {0}", ex);
				}
			}
		}

		protected void check_inferior ()
		{
			if (inferior == null)
				throw new TargetException (TargetError.NoTarget);
		}

		public IArchitecture Architecture {
			get { return arch; }
		}


		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;
			frames_invalid ();
		}

		// <summary>
		//   A breakpoint has been hit; now the sse needs to find out what do do:
		//   either ignore the breakpoint and continue or keep the target stopped
		//   and send out the notification.
		//
		//   If @index is zero, we hit an "unknown" breakpoint - ie. a
		//   breakpoint which we did not create.  Normally, this means that there
		//   is a breakpoint instruction (such as G_BREAKPOINT ()) in the code.
		//   Such unknown breakpoints are handled by the DebuggerBackend; one of
		//   the language backends may recognize the breakpoint's address, for
		//   instance if this is the JIT's breakpoint trampoline.
		//
		//   Returns true if the target should remain stopped and false to
		//   continue stepping.
		//
		//   If we can't find a handler for the breakpoint, the default is to stop
		//   the target and let the user decide what to do.
		// </summary>
		bool child_breakpoint (int index)
		{
			// The inferior knows about breakpoints from all threads, so if this is
			// zero, then no other thread has set this breakpoint.
			if (index == 0)
				return true;

			Breakpoint bpt = manager.BreakpointManager.LookupBreakpoint (index);
			if (bpt == null)
				return false;

			StackFrame frame = null;
			// Only compute the current stack frame if the handler actually
			// needs it.  Note that this computation is an expensive operation
			// so we should only do it when it's actually needed.
			if (bpt.HandlerNeedsFrame)
				frame = get_frame ();
			if (!bpt.CheckBreakpointHit (frame, inferior))
				return false;

			frame_changed (inferior.CurrentFrame, current_operation);
			bpt.BreakpointHit (frame);

			return true;
		}

		bool step_over_breakpoint (TargetAddress until, bool current)
		{
			int index;
			Breakpoint bpt = manager.BreakpointManager.LookupBreakpoint (
				inferior.CurrentFrame, out index);

			if ((index == 0) ||
			    (!current && (bpt != null) && bpt.Breaks (process.ID)))
				return false;

			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over {3}breakpoint {1} at {2}",
				      this, index, inferior.CurrentFrame,
				      current ? "current " : "");

			manager.AcquireGlobalThreadLock (this);
			inferior.DisableBreakpoint (index);

			stepping_over_breakpoint = index;
			if (until.IsNull)
				inferior.Step ();
			else {
				insert_temporary_breakpoint (until);
				inferior.Continue ();
			}
			return true;
		}

		protected IMethod current_method;
		protected StackFrame current_frame;
		protected Backtrace current_backtrace;
		protected Registers registers;

		public Backtrace CurrentBacktrace {
			get { return current_backtrace; }
		}

		public StackFrame CurrentFrame {
			get { return current_frame; }
		}

		public IMethod CurrentMethod {
			get { return current_method; }
		}

		// <summary>
		//   Compute the StackFrame for target address @address.
		// </summary>
		StackFrame get_frame ()
		{
			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();
			TargetAddress address = iframe.Address;

			// If we have a current_method and the address is still inside
			// that method, we don't need to do a method lookup.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				manager.DebuggerBackend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			registers = inferior.GetRegisters ();
			SimpleStackFrame simple = new SimpleStackFrame (iframe, registers, 0);
			current_frame = StackFrame.CreateFrame (
				process, simple, current_method);

			return current_frame;
		}

		// <summary>
		//   Check whether @address is inside @frame.
		// </summary>
		bool is_in_step_frame (StepFrame frame, TargetAddress address)
                {
			if (address.IsNull || frame.Start.IsNull)
				return false;

                        if ((address < frame.Start) || (address >= frame.End))
                                return false;

                        return true;
                }

		// <summary>
		//   This is called when a stepping operation is completed or something
		//   unexpected happened (received signal etc.).
		//
		//   Normally, we just compute the new StackFrame here, but we may also
		//   discover that we need to do one more stepping operation, see
		//   check_method_operation().
		// </summary>
		Operation frame_changed (TargetAddress address, Operation operation)
		{
			// Mark the current stack frame and backtrace as invalid.
			frames_invalid ();

			// Only do a method lookup if we actually need it.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				manager.DebuggerBackend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();
			registers = inferior.GetRegisters ();

			// Compute the current stack frame.
			if ((current_method != null) && current_method.HasSource) {
				SourceAddress source = current_method.Source.Lookup (address);

				// If check_method_operation() returns true, it already
				// started a stepping operation, so the target is
				// currently running.
				Operation new_operation = check_method_operation (
					address, current_method, source, operation);
				if (new_operation != null) {
					Report.Debug (DebugFlags.EventLoop,
						      "New operation: {0}", new_operation);
					return new_operation;
				}

				SimpleStackFrame simple = new SimpleStackFrame (
					iframe, registers, 0);
				current_frame = StackFrame.CreateFrame (
					process, simple, current_method, source);
			} else {
				SimpleStackFrame simple = new SimpleStackFrame (
					iframe, registers, 0);
				current_frame = StackFrame.CreateFrame (
					process, simple, current_symtab,
					current_simple_symtab);
			}

			return null;
		}

		// <summary>
		//   Checks whether to do a "method operation".
		//
		//   This is only used while doing a source stepping operation and ensures
		//   that we don't stop somewhere inside a method's prologue code or
		//   between two source lines.
		// </summary>
		Operation check_method_operation (TargetAddress address, IMethod method,
						  SourceAddress source, Operation operation)
		{
			if ((operation == null) || operation.IsNative)
				return null;

			if (method.IsWrapper && (address == method.StartAddress))
				return new Operation (OperationType.Run, method.WrapperAddress);

			ILanguageBackend language = method.Module.LanguageBackend as ILanguageBackend;
			if (source == null)
				return null;

			// Do nothing if this is not a source stepping operation.
			if (!operation.IsSourceOperation)
				return null;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				// We stopped between two source lines.  This normally
				// happens when returning from a method call; in this
				// case, we need to continue stepping until we reach the
				// next source line.
				return new Operation (OperationType.StepFrame, new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					language, operation.Type == OperationType.StepLine ?
					StepMode.StepFrame : StepMode.Finish));
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				// Do not stop inside a method's prologue code, but stop
				// immediately behind it (on the first instruction of the
				// method's actual code).
				return new Operation (OperationType.StepFrame, new StepFrame (
					method.StartAddress, method.MethodStartAddress,
					null, StepMode.Finish));
			}

			return null;
		}

		void frames_invalid ()
		{
			if (current_frame != null) {
				current_frame.Dispose ();
				current_frame = null;
			}

			if (current_backtrace != null) {
				current_backtrace.Dispose ();
				current_backtrace = null;
			}

			registers = null;
		}

		int temp_breakpoint_id = 0;
		void insert_temporary_breakpoint (TargetAddress address)
		{
			check_inferior ();
			int dr_index;
			temp_breakpoint_id = inferior.InsertHardwareBreakpoint (
				address, true, out dr_index);
		}

		// <summary>
		//   Single-step one machine instruction.
		// </summary>
		void do_step ()
		{
			check_inferior ();
			frames_invalid ();
			do_continue_internal (TargetAddress.Null, true);
		}

		// <summary>
		//   Step over the next machine instruction.
		// </summary>
		void do_next ()
		{
			check_inferior ();
			frames_invalid ();
			TargetAddress address = inferior.CurrentFrame;

			// Check whether this is a call instruction.
			int insn_size;
			TargetAddress call = arch.GetCallTarget (inferior, address, out insn_size);

			Report.Debug (DebugFlags.SSE, "{0} do_next: {1} {2}", this,
				      address, call);

			// Step one instruction unless this is a call
			if (call.IsNull) {
				do_step ();
				return;
			}

			// Insert a temporary breakpoint immediately behind it and continue.
			address += insn_size;
			do_continue (address);
		}

		// <summary>
		//   Resume the target.
		// </summary>
		void do_continue ()
		{
			do_continue (TargetAddress.Null, TargetAddress.Null);
		}

		void do_continue (TargetAddress until)
		{
			do_continue (TargetAddress.Null, until);
		}

		void do_continue (TargetAddress trampoline, TargetAddress until)
		{
			check_inferior ();
			frames_invalid ();
			if (!until.IsNull)
				insert_temporary_breakpoint (until);
			do_continue_internal (trampoline, false);
		}

		void do_continue_internal (TargetAddress trampoline, bool step)
		{
			if (step_over_breakpoint (trampoline, true))
				return;

			if (step)
				inferior.Step ();
			else
				inferior.Continue ();
		}

		Operation current_operation;

		protected bool Step (Operation operation)
		{
			check_inferior ();

			current_operation = null;
			frames_invalid ();

			Report.Debug (DebugFlags.SSE, "{0} starting {1}", this, operation);

			if ((operation.Type != OperationType.Run) &&
			    (operation.Type != OperationType.RunInBackground) &&
			    (operation.Type != OperationType.FinishNative) &&
			    (operation.StepFrame == null)) {
				do_step ();
				return true;
			}

			current_operation = operation;
			if (DoStep (true)) {
				Report.Debug (DebugFlags.SSE,
					      "{0} finished step operation", this);
				step_operation_finished ();
				return true;
			}
			return false;
		}

		void step_operation_finished ()
		{
			current_operation = null;
		}

		bool do_trampoline (StepFrame frame, TargetAddress trampoline, bool is_start)
		{
			TargetAddress compile = frame.Language.CompileMethodFunc;

			Report.Debug (DebugFlags.SSE,
				      "{0} found trampoline {1}/{3} (compile is {2})",
				      this, trampoline, compile, is_start);

			if (is_start) {
				current_operation = new Operation (OperationType.FinishNative);
				do_continue (trampoline);
				return false;
			}

			if (compile.IsNull) {
				IMethod method = null;
				if (current_symtab != null) {
					manager.DebuggerBackend.UpdateSymbolTable ();
					method = Lookup (trampoline);
				}
				if (!method_has_source (method)) {
					do_next ();
					return false;
				}

				do_continue (trampoline);
				return false;
			}

			do_callback (new CallbackCompileMethod (compile, trampoline));
			return false;
		}

		bool method_has_source (IMethod method)
		{
			if (method == null)
				return false;

			if (!method.HasSource || !method.Module.StepInto)
				return false;

			MethodSource source = method.Source;
			if ((source == null) || source.IsDynamic)
				return false;

			SourceFileFactory factory = manager.DebuggerBackend.SourceFileFactory;
			if (!factory.Exists (source.SourceFile.FileName))
				return false;

			SourceAddress addr = source.Lookup (method.MethodStartAddress);
			if (addr == null) {
				Console.WriteLine ("OOOOPS - No source for method: " +
						   "{0} {1} {2} - {3} {4}",
						   method, source, source.SourceFile.FileName,
						   source.StartRow, source.EndRow);
				source.DumpLineNumbers ();
				return false;
			}

			return true;
		}

		// <summary>
		//   If `first' is true, start a new stepping operation.
		//   Otherwise, we've already completed one or more atomic operations and
		//   need to find out whether we need another one.
		//   Returns true if the stepping operation is completed (and thus the
		//   target is still stopped) and false if it started another atomic
		//   operation.
		// </summary>
		protected bool DoStep (bool first)
		{
			if ((current_operation.Type == OperationType.Run) ||
			    (current_operation.Type == OperationType.RunInBackground)) {
				TargetAddress until = current_operation.Until;
				if (!until.IsNull)
					do_continue (until);
				else
					do_continue ();
				return false;
			}

			if (current_operation.Type == OperationType.FinishNative) {
				return do_finish_native (first);
			}

			StepFrame frame = current_operation.StepFrame;
			if (frame == null)
				return true;

			TargetAddress current_frame = inferior.CurrentFrame;
			bool in_frame = is_in_step_frame (frame, current_frame);
			Report.Debug (DebugFlags.SSE, "{0} stepping at {1} in {2} {3}",
				      this, current_frame, frame, in_frame);
			if (!first && !in_frame)
				return true;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the specified step frame.
			 */
			int insn_size;
			TargetAddress call = arch.GetCallTarget (inferior, current_frame, out insn_size);
			if (call.IsNull) {
				do_step ();
				return false;
			}

			/*
			 * If we have a source language, check for trampolines.
			 * This will trigger a JIT compilation if neccessary.
			 */
			if ((frame.Mode != StepMode.Finish) && (frame.Language != null)) {
				bool is_start;
				TargetAddress trampoline = frame.Language.GetTrampolineAddress (
					inferior, call, out is_start);
				IMethod tmethod = null;

				/*
				 * If this is a trampoline, insert a breakpoint at the start of
				 * the corresponding method and continue.
				 *
				 * We don't need to distinguish between StepMode.SingleInstruction
				 * and StepMode.StepFrame here since we'd leave the step frame anyways
				 * when entering the method.
				 */
				if (!trampoline.IsNull)
					return do_trampoline (frame, trampoline, is_start);

				if (frame.Mode != StepMode.SingleInstruction) {
					/*
					 * If this is an ordinary method, check whether we have
					 * debugging info for it and don't step into it if not.
					 */
					tmethod = Lookup (call);
					if (!method_has_source (tmethod)) {
						do_next ();
						return false;
					}
				}
			}

			/*
			 * When StepMode.SingleInstruction was requested, enter the method no matter
			 * whether it's a system function or not.
			 */
			if (frame.Mode == StepMode.SingleInstruction) {
				do_step ();
				return false;
			}

			/*
			 * In StepMode.Finish, always step over all methods.
			 */
			if (frame.Mode == StepMode.Finish) {
				do_next ();
				return false;
			}

			/*
			 * Try to find out whether this is a system function by doing a symbol lookup.
			 * If it can't be found in the symbol tables, assume it's a system function
			 * and step over it.
			 */
			IMethod method = Lookup (call);
			if (!method_has_source (method)) {
				do_next ();
				return false;
			}

			/*
			 * If this is a PInvoke/icall wrapper, check whether we want to step into
			 * the wrapped function.
			 */
			if (method.IsWrapper) {
				TargetAddress wrapper = method.WrapperAddress;
				IMethod wmethod = Lookup (wrapper);

				if (!method_has_source (wmethod)) {
					do_next ();
					return false;
				}

				do_continue (wrapper);
				return false;
			}

			/*
			 * Finally, step into the method.
			 */
			do_step ();
			return false;
		}

		// <summary>
		//   Create a step frame to step until the next source line.
		// </summary>
		StepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = current_frame;
			object language = (frame.Method != null) ? frame.Method.Module.LanguageBackend : null;

			if (frame.SourceAddress == null)
				return new StepFrame (language, StepMode.SingleInstruction);

			// The current source line started at the current address minus
			// SourceOffset; the next source line will start at the current
			// address plus SourceRange.

			int offset = frame.SourceAddress.SourceOffset;
			int range = frame.SourceAddress.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, language, StepMode.StepFrame);
		}

		// <summary>
		//   Create a step frame for a native stepping operation.
		// </summary>
		StepFrame get_step_frame (StepMode mode)
		{
			check_inferior ();
			object language;

			if (current_method != null)
				language = current_method.Module.LanguageBackend;
			else
				language = null;

			return new StepFrame (language, mode);
		}

		bool do_finish_native (bool first)
		{
			if (first) {
				do_step ();
				return false;
			}

			Inferior.StackFrame frame = inferior.GetCurrentFrame ();
			TargetAddress stack = frame.StackPointer;
			if (current_operation.Until.IsNull) {
				Report.Debug (DebugFlags.SSE,
					      "{0} starting finish native until {1}",
					      this, stack);
				current_operation = new Operation (
					OperationType.FinishNative, stack);
			}

			TargetAddress until = current_operation.Until;

			Report.Debug (DebugFlags.SSE, "{0} finish native: stack = {1}, " +
				      "until = {2}", this, stack, until);

			if (stack <= until) {
				do_next ();
				return false;
			}

			return true;
		}

		void restore_stack (StackData stack)
		{
			if (inferior.CurrentFrame != stack.Frame.TargetAddress) {
				Report.Debug (DebugFlags.EventLoop,
					      "{0} discarding saved stack: stopped " +
					      "at {1}, but recorded {2}", this,
					      inferior.CurrentFrame, stack.Frame.TargetAddress);
				frame_changed (inferior.CurrentFrame, null);
				return;
			}

			current_method = stack.Method;
			if ((current_frame != null) && (current_frame != stack.Frame)) {
				current_frame.Dispose ();
				if (current_backtrace != null)
					current_backtrace.Dispose ();
			}

			current_frame = stack.Frame;
			current_backtrace = stack.Backtrace;
			registers = stack.Registers;
			Report.Debug (DebugFlags.EventLoop,
				      "{0} restored stack: {1}", this, current_frame);
		}

		void do_callback (Callback cb)
		{
			if (current_callback != null)
				throw new InternalError ();

			current_callback = cb;
			cb.Execute (this, inferior);
		}

		Callback current_callback;

		protected delegate bool CallbackFunc (Callback cb, long data1, long data2);

		protected abstract class Callback
		{
			public readonly long ID = ++next_id;
			StackData stack_data;

			static int next_id = 0;

			public void Execute (SingleSteppingEngine sse,
					     Inferior inferior)
			{
				stack_data = sse.save_stack ();
				DoExecute (sse, inferior);
			}

			protected abstract void DoExecute (SingleSteppingEngine sse,
							   Inferior inferior);

			public bool ProcessEvent (SingleSteppingEngine sse,
						  Inferior inferior,
						  Inferior.ChildEvent cevent)
			{
				if (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK) {
					Abort ();
					return true;
				}

				if (ID != cevent.Argument) {
					Abort ();
					return true;
				}

				if (!DoProcessEvent (sse, inferior, cevent.Data1, cevent.Data2))
					return false;

				if (stack_data != null)
					sse.restore_stack (stack_data);
				else 
					sse.frame_changed (inferior.CurrentFrame, null);
				return true;
			}

			protected abstract bool DoProcessEvent (SingleSteppingEngine sse,
								Inferior inferior,
								long data1, long data2);

			public void Abort ()
			{
				if (stack_data == null)
					return;

				if (stack_data.Frame != null)
					stack_data.Frame.Dispose ();

				if (stack_data.Backtrace != null)
					stack_data.Backtrace.Dispose ();
			}
		}

		protected class CallbackCallMethod : Callback
		{
			CallMethodData data;

			public CallbackCallMethod (CallMethodData data)
			{
				this.data = data;
			}

			protected override void DoExecute (SingleSteppingEngine sse,
							   Inferior inferior)
			{
				switch (data.Type) {
				case CallMethodType.LongLong:
					inferior.CallMethod (data.Method, data.Argument1,
							     data.Argument2, ID);
					break;

				case CallMethodType.LongString:
					inferior.CallMethod (data.Method, data.Argument1,
							     data.StringArgument, ID);
					break;

				default:
					throw new InvalidOperationException ();
				}
			}

			protected override bool DoProcessEvent (SingleSteppingEngine sse,
								Inferior inferior,
								long data1, long data2)
			{
				data.Result = data1;

				Report.Debug (DebugFlags.EventLoop,
					      "Call method done: {0} {1:x} {2:x}",
					      sse, data1, data2);

				return true;
			}
		}

		protected class CallbackCompileMethod : Callback
		{
			TargetAddress compile;
			TargetAddress trampoline;

			public CallbackCompileMethod (TargetAddress compile,
						      TargetAddress trampoline)
			{
				this.compile = compile;
				this.trampoline = trampoline;
			}

			protected override void DoExecute (SingleSteppingEngine sse,
							   Inferior inferior)
			{
				inferior.CallMethod (compile, trampoline.Address, 0, ID);
			}

			protected override bool DoProcessEvent (SingleSteppingEngine sse,
								Inferior inferior,
								long data1, long data2)
			{
				TargetAddress trampoline = new TargetAddress (
					inferior.GlobalAddressDomain, data1);

				IMethod method = sse.do_lookup (trampoline);
				Report.Debug (DebugFlags.SSE,
					      "{0} compiled trampoline: {1} {2} {3} {4}",
					      this, trampoline, method,
					      method != null ? method.Module : null,
					      sse.method_has_source (method));

				if (!sse.method_has_source (method)) {
					sse.do_next ();
					return false;
				}

				Report.Debug (DebugFlags.SSE,
					      "{0} entering trampoline: {1}",
					      this, trampoline);

				sse.do_continue (trampoline, trampoline);
				return false;
			}
		}

		protected class CallbackRuntimeInvoke : Callback
		{
			RuntimeInvokeData rdata;
			bool method_compiled;

			public CallbackRuntimeInvoke (RuntimeInvokeData rdata)
			{
				this.rdata = rdata;
			}

			protected override void DoExecute (SingleSteppingEngine sse,
							   Inferior inferior)
			{
				inferior.CallMethod (rdata.Language.CompileMethodFunc,
						     rdata.MethodArgument.Address, 0, ID);
			}

			protected override bool DoProcessEvent (SingleSteppingEngine sse,
								Inferior inferior,
								long data1, long data2)
			{
				if (!method_compiled) {
					method_compiled = true;

					TargetAddress invoke = new TargetAddress (
						inferior.AddressDomain, data1);

					Report.Debug (DebugFlags.EventLoop,
						      "Runtime invoke: {0}", invoke);

					if (rdata.Debug)
						sse.insert_temporary_breakpoint (invoke);

					inferior.RuntimeInvoke (
						rdata.Language.RuntimeInvokeFunc,
						rdata.MethodArgument, rdata.ObjectArgument,
						rdata.ParamObjects, ID, rdata.Debug);
					return false;
				}

				Report.Debug (DebugFlags.EventLoop,
				      "Runtime invoke done: {0:x} {1:x}",
				      data1, data2);

				rdata.InvokeOk = true;
				if (data1 != 0)
					rdata.ReturnObject = new TargetAddress (
						inferior.AddressDomain, data1);
				else
					rdata.ReturnObject = TargetAddress.Null;
				if (data2 != 0)
					rdata.ExceptionObject = new TargetAddress (
						inferior.AddressDomain, data2);
				else
					rdata.ExceptionObject = TargetAddress.Null;

				return true;
			}
		}

		internal sealed class StackData
		{
			public readonly IMethod Method;
			public readonly StackFrame Frame;
			public readonly Backtrace Backtrace;
			public readonly Registers Registers;

			public StackData (IMethod method, StackFrame frame,
					  Backtrace backtrace, Registers registers)
			{
				this.Method = method;
				this.Frame = frame;
				this.Backtrace = backtrace;
				this.Registers = registers;
			}
		}

		StackData save_stack ()
		{
			//
			// Save current state.
			//
			StackData stack_data = new StackData (
				current_method, current_frame, current_backtrace, registers);

			current_method = null;
			current_frame = null;
			current_backtrace = null;
			registers = null;

			return stack_data;
		}

		bool stopped;
		Inferior.ChildEvent stop_event;

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		public void AcquireThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} acquiring thread lock", this);

			stopped = inferior.Stop (out stop_event);

			get_registers ();
			long esp = registers [(int) I386Register.ESP].Value;
			TargetAddress addr = new TargetAddress (AddressDomain, esp);

			Report.Debug (DebugFlags.Threads,
				      "{0} acquired thread lock: {1} {2} {3} {4}",
				      this, stopped, stop_event, EndStackAddress, addr);

			if (!EndStackAddress.IsNull)
				inferior.WriteAddress (EndStackAddress, addr);
		}

		public void ReleaseThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} releasing thread lock: {1} {2}",
				      this, stopped, stop_event);

			// If the target was already stopped, there's nothing to do for us.
			if (!stopped)
				return;
			if (stop_event != null) {
				// The target stopped before we were able to send the SIGSTOP,
				// but we haven't processed this event yet.
				Inferior.ChildEvent cevent = stop_event;
				stop_event = null;

				if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
				    (cevent.Argument == 0)) {
					do_continue ();
					return;
				}

				if (manager.HandleChildEvent (inferior, ref cevent))
					return;
				ProcessEvent (cevent);
			} else {
				do_continue ();
			}
		}

		public override string ToString ()
		{
			return String.Format ("SSE ({0}:{1}:{2:x})", process.ID, PID, TID);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("SingleSteppingEngine");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (inferior != null) {
					inferior.Kill ();
					inferior.Dispose ();
				}
				inferior = null;
			}

			disposed = true;
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~SingleSteppingEngine ()
		{
			Dispose (false);
		}
	}

	internal sealed class Operation {
		public readonly OperationType Type;
		public readonly TargetAddress Until;
		public readonly RuntimeInvokeData RuntimeInvokeData;
		public readonly CallMethodData CallMethodData;

		public Operation (OperationType type)
		{
			this.Type = type;
			this.Until = TargetAddress.Null;
		}

		public Operation (OperationType type, StepFrame frame)
		{
			this.Type = type;
			this.StepFrame = frame;
		}

		public Operation (OperationType type, TargetAddress until)
		{
			this.Type = type;
			this.Until = until;
		}

		public Operation (RuntimeInvokeData data)
		{
			this.Type = OperationType.RuntimeInvoke;
			this.RuntimeInvokeData = data;
		}

		public Operation (CallMethodData data)
		{
			this.Type = OperationType.CallMethod;
			this.CallMethodData = data;
		}

		public StepMode StepMode;
		public StepFrame StepFrame;

		public bool IsNative {
			get { return Type == OperationType.StepNativeInstruction; }
		}

		public bool IsSourceOperation {
			get {
				return (Type == OperationType.StepLine) ||
					(Type == OperationType.NextLine) ||
					(Type == OperationType.Run) ||
					(Type == OperationType.RunInBackground) ||
					(Type == OperationType.RuntimeInvoke) ||
					(Type == OperationType.Initialize);
			}
		}

		public override string ToString ()
		{
			if (StepFrame != null)
				return String.Format ("Operation ({0}:{1}:{2}:{3})",
						      Type, Until, StepMode, StepFrame);
			else if (!Until.IsNull)
				return String.Format ("Operation ({0}:{1})", Type, Until);
			else
				return String.Format ("Operation ({0})", Type);
		}
	}

	internal enum OperationType {
		Initialize,
		Run,
		RunInBackground,
		StepInstruction,
		StepNativeInstruction,
		NextInstruction,
		StepLine,
		NextLine,
		StepFrame,
		RuntimeInvoke,
		CallMethod,
		FinishNative,
		Exception
	}

	internal enum CommandType {
		Operation,
		GetBacktrace,
		GetRegisters,
		SetRegisters,
		InsertBreakpoint,
		RemoveBreakpoint,
		GetInstructionSize,
		DisassembleInstruction,
		DisassembleMethod,
		ReadMemory,
		ReadString,
		WriteMemory
	}

	internal class Command {
		public SingleSteppingEngine Process;
		public CommandType Type;
		public Operation Operation;
		public object Data1, Data2;

		public Command (SingleSteppingEngine process, Operation operation)
		{
			this.Process = process;
			this.Type = CommandType.Operation;
			this.Operation = operation;
		}

		public Command (SingleSteppingEngine process, CommandType type, object data, object data2)
		{
			this.Process = process;
			this.Type = type;
			this.Data1 = data;
			this.Data2 = data2;
		}

		public override string ToString ()
		{
			return String.Format ("Command ({0}:{1}:{2}:{3}:{4})",
					      Process, Type, Operation, Data1, Data2);
		}
	}

	internal enum CommandResultType {
		ChildEvent,
		CommandOk,
		NotStopped,
		Interrupted,
		UnknownError,
		Exception
	}

	internal class CommandResult {
		public readonly static CommandResult Ok = new CommandResult (CommandResultType.CommandOk, null);
		public readonly static CommandResult Busy = new CommandResult (CommandResultType.NotStopped, null);
		public readonly static CommandResult Interrupted = new CommandResult (CommandResultType.Interrupted, null);

		public readonly CommandResultType Type;
		public readonly Inferior.ChildEventType EventType;
		public readonly int Argument;
		public readonly object Data;

		public CommandResult (Inferior.ChildEventType type, int arg)
		{
			this.EventType = type;
			this.Argument = arg;
		}

		public CommandResult (Inferior.ChildEventType type, object data)
		{
			this.EventType = type;
			this.Argument = 0;
			this.Data = data;
		}

		public CommandResult (CommandResultType type, object data)
		{
			this.Type = type;
			this.Data = data;
		}

		public CommandResult (Exception e)
		{
			this.Type = CommandResultType.Exception;
			this.Data = e;
		}

		public override string ToString ()
		{
			return String.Format ("CommandResult ({0}:{1}:{2}:{3})",
					      Type, EventType, Argument, Data);
		}
	}

	internal enum CallMethodType
	{
		LongLong,
		LongString
	}

	internal sealed class CallMethodData
	{
		public readonly CallMethodType Type;
		public readonly TargetAddress Method;
		public readonly long Argument1;
		public readonly long Argument2;
		public readonly string StringArgument;
		public readonly object Data;
		public object Result;

		public CallMethodData (TargetAddress method, long arg, string sarg,
				       object data)
		{
			this.Type = CallMethodType.LongString;
			this.Method = method;
			this.Argument1 = arg;
			this.Argument2 = 0;
			this.StringArgument = sarg;
			this.Data = data;
		}

		public CallMethodData (TargetAddress method, long arg1, long arg2,
				       object data)
		{
			this.Type = CallMethodType.LongLong;
			this.Method = method;
			this.Argument1 = arg1;
			this.Argument2 = arg2;
			this.StringArgument = null;
			this.Data = data;
		}
	}

	internal sealed class RuntimeInvokeData
	{
		public readonly ILanguageBackend Language;
		public readonly TargetAddress MethodArgument;
		public readonly TargetAddress ObjectArgument;
		public readonly TargetAddress[] ParamObjects;

		public bool Debug;
		public bool InvokeOk;
		public TargetAddress ReturnObject;
		public TargetAddress ExceptionObject;

		public RuntimeInvokeData (ILanguageBackend language,
					  TargetAddress method_argument,
					  TargetAddress object_argument,
					  TargetAddress[] param_objects)
		{
			this.Language = language;
			this.MethodArgument = method_argument;
			this.ObjectArgument = object_argument;
			this.ParamObjects = param_objects;
		}
	}
}
