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
	public delegate bool BreakpointHitHandler (StackFrame frame, int index, object user_data);

	// <summary>
	//   The single stepping engine is responsible for doing all the stepping
	//   operations.
	//
	//     sse                  - short for single stepping engine.
	//
	//     stepping operation   - an operation which has been invoked by the user such
	//                            as StepLine(), NextLine() etc.
	//
	//     target operation     - an operation which the sse invokes on the target
	//                            such as stepping one machine instruction or resuming
	//                            the target until a breakpoint is hit.
	//
	//     step frame           - an address range; the sse invokes target operations
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
	public class SingleSteppingEngine : ITargetMemoryAccess, IDisposable
	{
		public SingleSteppingEngine (DebuggerBackend backend, Process process,
					     IInferior inferior, bool native)
		{
			this.backend = backend;
			this.process = process;
			this.symtab_manager = backend.SymbolTableManager;
			this.inferior = inferior;
			this.native = native;

			thread_manager = backend.ThreadManager;
			breakpoint_manager = thread_manager.BreakpointManager;

			inferior.SingleSteppingEngine = this;
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			step_event = new AutoResetEvent (false);
			start_event = new ManualResetEvent (false);
			completed_event = new ManualResetEvent (false);
			restart_event = new AutoResetEvent (false);
			stop_event = new AutoResetEvent (false);
			thread_notify = new ThreadNotify (new ReadyEventHandler (ready_event_handler));
			command_mutex = new Mutex ();
		}

		// <summary>
		//   This is called in two situations:
		//   a) From the glib main loop if an async operation completed
		//   b) Before returning to the caller when a synchronous operation is completed
		//   In either case, it's guaranteed that this method will never be called
		//   from the background thread.
		// </summary>
		void ready_event_handler ()
		{
			lock (this) {
				if (command_result == null)
					return;

				if (command_result.Type != CommandResultType.ChildEvent)
					throw new InternalError ();

				switch (command_result.EventType) {
				case ChildEventType.CHILD_HIT_BREAKPOINT:
					change_target_state (TargetState.STOPPED, 0);
					break;

				case ChildEventType.CHILD_SIGNALED:
				case ChildEventType.CHILD_EXITED:
					change_target_state (TargetState.EXITED, command_result.Argument);
					break;

				default:
					change_target_state (TargetState.STOPPED, command_result.Argument);
					break;
				}

				command_result = null;
			}
		}

		// <summary>
		//   Start the application and - if `synchronous' is true - wait until it
		//   stopped the first time.  In either case, this function blocks until
		//   the application has actually been launched.
		// </summary>
		public void Run (bool synchronous)
		{
			if (engine_thread != null)
				throw new AlreadyHaveTargetException ();

			engine_thread = new Thread (new ThreadStart (start_engine_thread));
			engine_thread.Start ();

			wait_until_engine_is_ready ();
			if (synchronous)
				wait_for_completion ();
		}

		// <summary>
		//   Attach to `pid' and - if `synchronous' is true - wait until it
		//   actually stopped.  In either case, this function blocks until the
		//   application has actually been launched.
		// </summary>
		public void Attach (int pid, bool synchronous)
		{
			if (engine_thread != null)
				throw new AlreadyHaveTargetException ();

			this.pid = pid;

			reached_main = true;
			reached_main_2 = true;
			initialized = true;

			engine_thread = new Thread (new ThreadStart (start_engine_thread_attach));
			engine_thread.Start ();

			wait_until_engine_is_ready ();
			if (synchronous)
				wait_for_completion ();
		}

		// <remarks>
		//   This is only called on startup and blocks until the background thread
		//   has actually been started and it's waiting for commands.
		// </summary>
		void wait_until_engine_is_ready ()
		{
			while (!start_event.WaitOne ())
				;

			ready_event_handler ();

			symtab_manager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);
		}

		// <summary>
		//   The `send_result' methods are used to send the result of a command
		//   from the background thread back to the caller.  This'll also wake up
		//   the glib main loop so it can call the `ready_event_handler'.
		// </summary>
		void send_result (ChildEventType message, int arg)
		{
			lock (this) {
				command_result = new CommandResult (message, arg);
				thread_notify.Signal ();
				result_sent = true;
				completed_event.Set ();
			}
		}

		void send_result (CommandResult result)
		{
			lock (this) {
				command_result = result;
				completed_event.Set ();
			}
		}

		void start_engine_thread ()
		{
			inferior.Run ();

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			initialized = true;

			TargetAddress main = TargetAddress.Null;
			main = inferior.MainMethodAddress;
			pid = inferior.PID;

			engine_thread_main (new Command (StepOperation.Run, main));
		}

		void start_engine_thread_attach ()
		{
			inferior.Attach (pid);

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			initialized = true;

			engine_thread_main (null);
		}

		void engine_ready ()
		{
			lock (this) {
				if (!result_sent) {
					frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
					send_result (ChildEventType.CHILD_STOPPED, 0);
				}

				start_event.Set ();
			}
		}

		// <summary>
		//   The heart of the SingleSteppingEngine.  This runs in a background
		//   thread and processes stepping commands.
		// </summary>
		void engine_thread_main (Command command)
		{
			bool first = true;

			do {
				try {
					process_command (command);
				} catch (ThreadAbortException e) {
					// We're exiting here.
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0}", e);
				}

				// If we reach this point the first time, signal our
				// caller that we're now ready and about to wait for commands.
				if (first) {
					engine_ready ();
					first = false;
				}

				// Wait until we get a command.
				while (!step_event.WaitOne ())
					;

				lock (this) {
					command = current_command;
					current_command = null;
				}
			} while (true);
		}

		ChildEvent wait ()
		{
			ChildEvent child_event;
			do {
				child_event = inferior.Wait ();
			} while (child_event == null);
			return child_event;
		}

		// <summary>
		//   Waits until the target stopped and returns false if it already sent
		//   the result back to the caller (this normally happens on an error
		//   where some sub-method already sent an error message back).  Normally
		//   this method will return true to signal the main loop that it still
		//   needs to send the result.
		//   `must_continue' specifies what to do when we stopped unexpectedly because
		//   of a signal or breakpoint and its handler told us to resume execution.
		//   If false, our caller is doing a single-instruction step operation and thus
		//   we can just report a successful completion.  Otherwise, resume the target.
		// </summary>
		// <remarks>
		//   This method may only be used in the background thread.
		// </remarks>
		bool process_child_event (ChildEvent child_event, bool must_continue)
		{
		again:
			ChildEventType message = child_event.Type;
			int arg = child_event.Argument;

			if (stop_requested) {
				stop_event.Set ();
				restart_event.WaitOne ();
				// A stop was requested and we actually received the SIGSTOP.  Note that
				// we may also have stopped for another reason before receiving the SIGSTOP.
				if ((message == ChildEventType.CHILD_STOPPED) && (arg == inferior.StopSignal))
					goto done;
				// Ignore the next SIGSTOP.
				pending_sigstop++;
			}

			if ((message == ChildEventType.CHILD_STOPPED) && (arg != 0)) {
				if ((pending_sigstop > 0) && (arg == inferior.StopSignal)) {
					--pending_sigstop;
					goto done;
				}
				if (!backend.SignalHandler (process, arg))
					goto done;
			}

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
				if ((message == ChildEventType.CHILD_EXITED) ||
				    (message == ChildEventType.CHILD_SIGNALED))
					// we can't remove the breakpoint anymore after
					// the target exited, but we need to clear this id.
					temp_breakpoint_id = 0;
				else if (message == ChildEventType.CHILD_HIT_BREAKPOINT) {
					if (arg == temp_breakpoint_id) {
						// we hit the temporary breakpoint; this'll always
						// happen in the `correct' thread since the
						// `temp_breakpoint_id' is only set in this
						// SingleSteppingEngine and not in any other thread's.
						message = ChildEventType.CHILD_STOPPED;
						arg = 0;
					}
				}
			}

			if (message == ChildEventType.CHILD_HIT_BREAKPOINT) {
				ChildEvent new_event;
				// Ok, the next thing we need to check is whether this is actually "our"
				// breakpoint or whether it belongs to another thread.  In this case,
				// `step_over_breakpoint' does everything for us and we can just continue
				// execution.
				if (step_over_breakpoint (false, out new_event)) {
					int new_arg = new_event.Argument;
					// If the child stopped normally, just continue its execution
					// here; otherwise, we need to deal with the unexpected stop.
					if ((new_event.Type != ChildEventType.CHILD_STOPPED) ||
					    ((new_arg != 0) && (new_arg != inferior.StopSignal))) {
						child_event = new_event;
						goto again;
					}
					goto done;
				} else if (!child_breakpoint (arg)) {
					// we hit any breakpoint, but its handler told us
					// to resume the target and continue.
					goto done;
				}
			}

			if (temp_breakpoint_id != 0) {
				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}

			switch (message) {
			case ChildEventType.CHILD_STOPPED:
				if (arg != 0) {
					frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
					send_result (message, arg);
					return false;
				}

				return true;

			case ChildEventType.CHILD_HIT_BREAKPOINT:
				return true;

			default:
				send_result (message, arg);
				return false;
			}

		done:
			if (must_continue) {
				child_event = do_continue_internal (true);
				goto again;
			}

			return true;
		}

		// <summary>
		//   The heart of the SingleSteppingEngine's background thread - process a
		//   command and send the result back to the caller.
		// </summary>
		void process_command (Command command)
		{
			bool ok;

			if (command == null)
				return;

			if (command.Type == CommandType.StepOperation)
				goto step_operation;

			// These are synchronous commands; ie. the caller blocks on us
			// until we finished the command and sent the result.
			send_result (command.CommandFunc (command.CommandFuncData));
			return;

		step_operation:
			frames_invalid ();
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();

		again:
			// Process another stepping command.
			switch (command.Operation) {
			case StepOperation.Run:
			case StepOperation.RunInBackground:
				TargetAddress until = command.Until;
				if (!until.IsNull)
					insert_temporary_breakpoint (until);
				ok = do_continue ();
				break;

			case StepOperation.StepInstruction:
				ok = do_step ();
				break;

			case StepOperation.NextInstruction:
				ok = do_next ();
				break;

			case StepOperation.StepLine:
			case StepOperation.NextLine:
				ok = Step (command.StepFrame);
				break;

			default:
				ok = Step (command.StepFrame);
				break;
			}

			// If `ok' is false, then the target stopped abnormally and one of
			// the sub-methods already sent the error message to the caller.
			if (!ok)
				return;

			//
			// Ok, the target stopped normally.  Now we need to compute the
			// new stack frame and then send the result to our caller.
			//

			if (initialized && !reached_main) {
				backend.ReachedMain (process);
				reached_main = true;

				if (!native) {
					ok = do_continue ();
					if (!ok)
						return;
				}
			}

			if (initialized && !reached_main_2) {
				main_method_retaddr = inferior.GetReturnAddress ();
				reached_main_2 = true;
			}

			TargetAddress frame = inferior.CurrentFrame;

			// After returning from `main', resume the target and keep
			// running until it exits (or hit a breakpoint or receives
			// a signal).
			if (!main_method_retaddr.IsNull && (frame == main_method_retaddr)) {
				ok = do_continue ();
				return;
			}

			// `frame_changed' computes the new stack frame - and it may also
			// send us a new step command.  This happens for instance, if we
			// stopped within a method's prologue or epilogue code.
			Command new_command = frame_changed (frame, 0, command.Operation);
			if (new_command != null) {
				command = new_command;
				goto again;
			}
			send_result (ChildEventType.CHILD_STOPPED, 0);
		}

		CommandResult reload_symtab (object data)
		{
			frames_invalid ();
			current_method = null;
			frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
			return new CommandResult (CommandResultType.CommandOk);
		}

		void update_symtabs (object sender, ISymbolTable symbol_table)
		{
			disassembler.SymbolTable = symbol_table;
			current_symtab = symbol_table;

			send_sync_command (new CommandFunc (reload_symtab), null);
		}

		public IMethod Lookup (TargetAddress address)
		{
			if (current_symtab == null)
				return null;

			return current_symtab.Lookup (address);
		}

		// <summary>
		//   This event is emitted each time a stepping operation is started or
		//   completed.  Other than the IInferior's StateChangedEvent, it is only
		//   emitted after the whole operation completed.
		// </summary>
		public event StateChangedHandler StateChangedEvent;

		// <summary>
		//   These four events are emitted from the background thread.
		// </summary>
		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		// <summary>
		//   The single-stepping engine's target state.  This will be
		//   TargetState.RUNNING while the engine is stepping.
		// </summary>
		public TargetState State {
			get {
				return target_state;
			}
		}

		// <summary>
		//   The current stack frame.  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public StackFrame CurrentFrame {
			get {
				check_inferior ();
				return current_frame;
			}
		}

		// <summary>
		//   The current stack frame.  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The backtrace is generated on
		//   demand, when this function is called.  However, the single stepping
		//   engine will compute this only once each time a stepping operation is
		//   completed.  This means that if you call this function several times
		//   without doing any stepping operations in the meantime, you'll always
		//   get the same backtrace.
		// </summary>
		public Backtrace GetBacktrace ()
		{
			check_inferior ();

			if (current_backtrace != null)
				return current_backtrace;

			send_sync_command (new CommandFunc (get_backtrace), null);

			return current_backtrace;
		}

		CommandResult get_backtrace (object data)
		{
			backend.UpdateSymbolTable ();

			IInferiorStackFrame[] iframes = inferior.GetBacktrace (-1, main_method_retaddr);
			StackFrame[] frames = new StackFrame [iframes.Length];
			MyBacktrace backtrace = new MyBacktrace (this);

			for (int i = 0; i < iframes.Length; i++) {
				TargetAddress address = iframes [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceLocation source = method.Source.Lookup (address);
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i], backtrace, source, method);
				} else
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i], backtrace);
			}

			backtrace.SetFrames (frames);
			current_backtrace = backtrace;
			return new CommandResult (CommandResultType.CommandOk);
		}

		public Register[] GetRegisters ()
		{
			check_inferior ();

			if (registers != null)
				return registers;

			send_sync_command (new CommandFunc (get_registers), null);

			return registers;
		}

		CommandResult get_registers (object data)
		{
			long[] regs = inferior.GetRegisters (arch.AllRegisterIndices);
			registers = new Register [regs.Length];
			for (int i = 0; i < regs.Length; i++)
				registers [i] = new Register (arch.AllRegisterIndices [i], regs [i]);
			return new CommandResult (CommandResultType.CommandOk);
		}

		// <summary>
		//   The current method  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public IMethod CurrentMethod {
			get {
				check_inferior ();
				return current_method;
			}
		}

		IInferior inferior;
		IArchitecture arch;
		DebuggerBackend backend;
		ThreadManager thread_manager;	
		BreakpointManager breakpoint_manager;
		Process process;
		IDisassembler disassembler;
		SymbolTableManager symtab_manager;
		ISymbolTable current_symtab;
		Thread engine_thread;
		ManualResetEvent start_event;
		ManualResetEvent completed_event;
		AutoResetEvent stop_event;
		AutoResetEvent restart_event;
		AutoResetEvent step_event;
		ThreadNotify thread_notify;
		Mutex command_mutex;
		bool stop_requested = false;
		bool result_sent = false;
		bool native;
		int pending_sigstop = 0;
		int pid = -1;

		TargetAddress main_method_retaddr = TargetAddress.Null;
		TargetState target_state = TargetState.NO_TARGET;

		TargetState change_target_state (TargetState new_state)
		{
			return change_target_state (new_state, 0);
		}

		bool in_event = false;

		// <summary>
		//   Called when a stepping operation is started and completed to send the
		//   StateChangedEvent.
		// </summary>
		TargetState change_target_state (TargetState new_state, int arg)
		{
			lock (this) {
				if (new_state == target_state)
					return target_state;

				TargetState old_state = target_state;
				target_state = new_state;

				in_event = true;

				if (StateChangedEvent != null)
					StateChangedEvent (target_state, arg);

				in_event = false;

				return old_state;
			}
		}

		void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		protected IArchitecture Architecture {
			get { return arch; }
		}

		// <summary>
		//   This method does two things:
		//   a) It acquires the `command_mutex' (and blocks until it gets it).
		//   b) After that, it attempts to acquire the `completed_event' as well
		//      and returns whether it was able to do so (without blocking).
		//   The caller must
		//   a) If this method returned true, release the `completed_event' before
		//      releasing the `command_mutex'.
		//   b) In any case release the `command_mutex'.
		// </summary>
		// <remarks>
		//   This is called before actually sending a stepping command to the
		//   background thread.  First, we need to acquire the `command_mutex' -
		//   this ensures that no synchronous command is running and that no other
		//   thread has queued an async operation.  Once we acquired the mutex, no
		//   other thread can issue any commands, but there may still be an async
		//   command running, so we also need to acquire the `completed_event'.
		//
		//   Note that acquiring the `command_mutex' only blocks if any other
		//   thread is about to send a command to the background thread; it'll not
		//   block if an asynchronous operation is still running.
		//
		//   The `command_mutex' is basically a `lock (this)', but the background
		//   thread needs to `lock (this)' itself to write the result, so we
		//   cannot use this here.
		//
		//   The `completed_event' is only unset if an async operation is
		//   currently running.
		// </remarks>
		bool check_can_run ()
		{
			check_inferior ();
			if (!command_mutex.WaitOne (0, false) || !completed_event.WaitOne (0, false))
				return false;
			return true;
		}

		bool start_native ()
		{
			if (!native)
				return false;

			TargetAddress main = inferior.MainMethodAddress;
			if (main.IsNull)
				return false;

			insert_temporary_breakpoint (main);
			return true;
		}

		void child_exited ()
		{
			inferior = null;
			frames_invalid ();
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		bool initialized
;
		bool reached_main;
		bool reached_main_2;
		bool debugger_info_read;

		private enum StepOperation {
			None,
			Native,
			Run,
			RunInBackground,
			StepInstruction,
			NextInstruction,
			StepLine,
			NextLine,
			StepFrame
		}

		private delegate CommandResult CommandFunc (object data);

		private enum CommandType {
			StepOperation,
			Command
		}

		private class Command {
			public CommandType Type;
			public StepOperation Operation;
			public StepFrame StepFrame;
			public TargetAddress Until;
			public CommandFunc CommandFunc;
			public object CommandFuncData;

			public Command (StepOperation operation, StepFrame frame)
			{
				this.Type = CommandType.StepOperation;
				this.Operation = operation;
				this.StepFrame = frame;
				this.Until = TargetAddress.Null;
			}

			public Command (StepOperation operation, TargetAddress until)
			{
				this.Type = CommandType.StepOperation;
				this.Operation = operation;
				this.StepFrame = null;
				this.Until = until;
			}

			public Command (StepOperation operation)
			{
				this.Type = CommandType.StepOperation;
				this.Operation = operation;
				this.StepFrame = null;
				this.Until = TargetAddress.Null;
			}

			public Command (CommandFunc func, object data)
			{
				this.Type = CommandType.Command;
				this.CommandFunc = func;
				this.CommandFuncData = data;
			}
		}

		private enum CommandResultType {
			ChildEvent,
			CommandOk,
			UnknownError,
			Exception
		}

		private class CommandResult {
			public readonly CommandResultType Type;
			public readonly ChildEventType EventType;
			public readonly int Argument;
			public readonly object Data;

			public CommandResult (ChildEventType type, int arg)
			{
				this.EventType = type;
				this.Argument = arg;
			}

			public CommandResult (CommandResultType type)
				: this (type, null)
			{ }

			public CommandResult (CommandResultType type, object data)
			{
				this.Type = type;
				this.Data = data;
			}
		}

		// <remarks>
		//   These two variables are shared between the two threads, so you need to
		//   lock (this) before accessing/modifying them.
		// </remarks>
		Command current_command = null;
		CommandResult command_result = null;

		// <summary>
		//   A breakpoint has been hit; now the sse needs to find out what do do:
		//   either ignore the breakpoint and continue or keep the target stopped
		//   and send out the notification.
		//
		//   If @breakpoint is zero, we hit an "unknown" breakpoint - ie. a
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
		bool child_breakpoint (int breakpoint)
		{
			// The inferior knows about breakpoints from all threads, so if this is
			// zero, then no other thread has set this breakpoint.
			if (breakpoint == 0) {
				if (reached_main_2)
					return backend.BreakpointHit (inferior, inferior.CurrentFrame);
				else
					return true;
			}

			if (!breakpoints.Contains (breakpoint))
				return true;

			BreakpointHandle handle = (BreakpointHandle) breakpoints [breakpoint];
			StackFrame frame = null;
			// Only compute the current stack frame if the handler actually
			// needs it.  Note that this computation is an expensive operation
			// so we should only do it when it's actually needed.
			if (handle.NeedsFrame)
				frame = get_frame (inferior.CurrentFrame);
			return handle.Handler (frame, breakpoint, handle.UserData);
		}

		bool step_over_breakpoint (bool current, out ChildEvent new_event)
		{
			int owner;
			int id = breakpoint_manager.LookupBreakpoint (inferior.CurrentFrame, out owner);

			new_event = null;

			if (id == 0)
				return false;

			if (!current && ((owner == 0) || (owner == pid)))
				return false;

			thread_manager.AcquireGlobalThreadLock (process);
			inferior.DisableBreakpoint (id);
			inferior.Step ();
			do {
				new_event = inferior.Wait ();
			} while (new_event == null);
			inferior.EnableBreakpoint (id);
			thread_manager.ReleaseGlobalThreadLock (process);
			return true;
		}

		IMethod old_method;
		IMethod current_method;
		StackFrame current_frame;
		Backtrace current_backtrace;
		Register[] registers;

		// <summary>
		//   Compute the StackFrame for target address @address.
		// </summary>
		StackFrame get_frame (TargetAddress address)
		{
			// If we have a current_method and the address is still inside
			// that method, we don't need to do a method lookup.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				backend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			// This gets just one single stack frame.
			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			if ((current_method != null) && current_method.HasSource) {
				SourceLocation source = current_method.Source.Lookup (address);

				current_frame = new MyStackFrame (
					this, address, 0, frames [0], null, source, current_method);
			} else
				current_frame = new MyStackFrame (
					this, address, 0, frames [0], null);

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
		//   This is called when the target left the current step frame, received
		//   a signal or hit a breakpoint whose action was to remain stopped.
		//
		//   The `current_operation' is the currently running stepping operation
		//   and `current_operation_frame' is a step frame corresponding to this
		//   operation.  In this method, we check whether the stepping operation
		//   has been completed or what to do next.
		// </summary>
		Command frame_changed (TargetAddress address, int arg, StepOperation operation)
		{
			// Mark the current stack frame and backtrace as invalid.
			frames_invalid ();

			// Only do a method lookup if we actually need it.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				backend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			// Compute the current stack frame.
			if ((current_method != null) && current_method.HasSource) {
				SourceLocation source = current_method.Source.Lookup (address);

				// If check_method_operation() returns true, it already
				// started a stepping operation, so the target is
				// currently running.
				Command new_command = check_method_operation (
					address, current_method, source, operation);
				if (new_command != null)
					return new_command;

				current_frame = new MyStackFrame (
					this, address, 0, frames [0], null, source, current_method);
			} else
				current_frame = new MyStackFrame (
					this, address, 0, frames [0], null);

			// If the method changed, notify our clients.
			if (current_method != old_method) {
				old_method = current_method;
				if (current_method != null) {
					if (MethodChangedEvent != null)
						MethodChangedEvent (current_method);
				} else {
					if (MethodInvalidEvent != null)
						MethodInvalidEvent ();
				}
			}

			if (FrameChangedEvent != null)
				FrameChangedEvent (current_frame);

			return null;
		}

		// <summary>
		//   Checks whether to do a "method operation".
		//
		//   This is only used while doing a source stepping operation and ensures
		//   that we don't stop somewhere inside a method's prologue code or
		//   between two source lines.
		// </summary>
		Command check_method_operation (TargetAddress address, IMethod method,
						SourceLocation source, StepOperation operation)
		{
			ILanguageBackend language = method.Module.Language as ILanguageBackend;
			if (language == null)
				return null;

			// Do nothing if this is not a source stepping operation.
			if ((operation != StepOperation.StepLine) &&
			    (operation != StepOperation.NextLine) &&
			    (operation != StepOperation.Run) &&
			    (operation != StepOperation.RunInBackground))
				return null;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				// We stopped between two source lines.  This normally
				// happens when returning from a method call; in this
				// case, we need to continue stepping until we reach the
				// next source line.
				return new Command (StepOperation.Native, new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					language, operation == StepOperation.StepLine ?
					StepMode.StepFrame : StepMode.Finish));
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				// Do not stop inside a method's prologue code, but stop
				// immediately behind it (on the first instruction of the
				// method's actual code).
				return new Command (StepOperation.Native, new StepFrame (
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
			temp_breakpoint_id = inferior.InsertBreakpoint (address);
		}

		// <summary>
		//   Single-step one machine instruction.
		// </summary>
		bool do_step ()
		{
			check_inferior ();
			ChildEvent child_event = do_continue_internal (false);
			return process_child_event (child_event, false);
		}

		// <summary>
		//   Step over the next machine instruction.
		// </summary>
		bool do_next ()
		{
			check_inferior ();
			TargetAddress address = inferior.CurrentFrame;
			if (arch.IsRetInstruction (address))
				// If this is a `ret' instruction, step one instruction.
				return do_step ();

			// Get the size of the current instruction, insert a temporary
			// breakpoint immediately behind it and continue.
			address += disassembler.GetInstructionSize (address);

			insert_temporary_breakpoint (address);
			return do_continue ();
		}

		// <summary>
		//   Resume the target.  If @is_breakpoint is true, the current
		//   instruction is a breakpoint; in this case we need to step one
		//   instruction before we can resume the target (see `must_continue' in
		//   child_event() for more info).
		// </summary>
		bool do_continue ()
		{
			check_inferior ();
			ChildEvent child_event = do_continue_internal (true);
			return process_child_event (child_event, true);
		}

		ChildEvent do_continue_internal (bool do_run)
		{
			check_inferior ();

			ChildEvent new_event;
			if (step_over_breakpoint (true, out new_event)) {
				int new_arg = new_event.Argument;
				// If the child stopped normally, just continue its execution
				// here; otherwise, we need to deal with the unexpected stop.
				if ((new_event.Type != ChildEventType.CHILD_STOPPED) ||
				    ((new_arg != 0) && (new_arg != inferior.StopSignal)))
					return new_event;
				else if (!do_run)
					return new_event;
			}

			if (do_run)
				inferior.Continue ();
			else
				inferior.Step ();

			return wait ();
		}

		protected bool Step (StepFrame frame)
		{
			check_inferior ();

			/*
			 * If no step frame is given, just step one machine instruction.
			 */
			if (frame == null)
				return do_step ();

			/*
			 * Step one instruction, but step over function calls.
			 */
			if (frame.Mode == StepMode.NextInstruction)
				return do_next ();

			bool first = true;
			do {
				TargetAddress current_frame = inferior.CurrentFrame;

				if (first)
					first = false;
				else if (!is_in_step_frame (frame, current_frame))
					return true;

				/*
				 * If this is not a call instruction, continue stepping until we leave
				 * the specified step frame.
				 */
				int insn_size;
				TargetAddress call = arch.GetCallTarget (current_frame, out insn_size);
				if (call.IsNull) {
					if (!do_step ())
						return false;
					continue;
				}

				/*
				 * If we have a source language, check for trampolines.
				 * This will trigger a JIT compilation if neccessary.
				 */
				if ((frame.Mode != StepMode.Finish) && (frame.Language != null)) {
					TargetAddress trampoline = frame.Language.GetTrampoline (inferior, call);

					/*
					 * If this is a trampoline, insert a breakpoint at the start of
					 * the corresponding method and continue.
					 *
					 * We don't need to distinguish between StepMode.SingleInstruction
					 * and StepMode.StepFrame here since we'd leave the step frame anyways
					 * when entering the method.
					 */
					if (!trampoline.IsNull) {
						IMethod tmethod = null;
						if (current_symtab != null) {
							backend.UpdateSymbolTable ();
							tmethod = Lookup (trampoline);
						}
						if ((tmethod == null) || !tmethod.Module.StepInto) {
							if (!do_next ())
								return false;
							continue;
						}

						insert_temporary_breakpoint (trampoline);
						return do_continue ();
					}
				}

				/*
				 * When StepMode.SingleInstruction was requested, enter the method no matter
				 * whether it's a system function or not.
				 */
				if (frame.Mode == StepMode.SingleInstruction)
					return do_step ();

				/*
				 * In StepMode.Finish, always step over all methods.
				 */
				if (frame.Mode == StepMode.Finish) {
					if (!do_next ())
						return false;
					continue;
				}

				/*
				 * Try to find out whether this is a system function by doing a symbol lookup.
				 * If it can't be found in the symbol tables, assume it's a system function
				 * and step over it.
				 */
				IMethod method = Lookup (call);
				if ((method == null) || !method.Module.StepInto) {
					if (!do_next ())
						return false;
					continue;
				}

				/*
				 * Finally, step into the method.
				 */
				return do_step ();
			} while (true);
		}

		// <summary>
		//   Create a step frame to step until the next source line.
		// </summary>
		StepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			object language = (frame.Method != null) ? frame.Method.Module.Language : null;

			if (frame.SourceLocation == null)
				return null;

			// The current source line started at the current address minus
			// SourceOffset; the next source line will start at the current
			// address plus SourceRange.

			int offset = frame.SourceLocation.SourceOffset;
			int range = frame.SourceLocation.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, language, StepMode.StepFrame);
		}

		// <summary>
		//   Create a step frame for a native stepping operation.
		// </summary>
		StepFrame get_simple_step_frame (StepMode mode)
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			object language;

			if (frame != null)
				language = (frame.Method != null) ? frame.Method.Module.Language : null;
			else
				language = null;

			return new StepFrame (language, mode);
		}

		// <summary>
		//   Sends an asynchronous command to the background thread.  This is used
		//   for all stepping commands, no matter whether the user requested a
		//   synchronous or asynchronous operation since we can just block on the
		//   `completed_event' in the synchronous case.
		// </summary>
		void send_async_command (Command command)
		{
			lock (this) {
				change_target_state (TargetState.RUNNING, 0);
				current_command = command;
				step_event.Set ();
				completed_event.Reset ();
			}
		}

		// <summary>
		//   Sends a synchronous command to the background thread.  This is only
		//   used for non-steping commands such as getting a backtrace.
		// </summary>
		CommandResult send_sync_command (CommandFunc func, object data)
		{
			if (Thread.CurrentThread == engine_thread)
				return func (data);

			if (!check_can_run ())
				return new CommandResult (CommandResultType.UnknownError);

			lock (this) {
				current_command = new Command (func, data);
				step_event.Set ();
				completed_event.Reset ();
			}

			completed_event.WaitOne ();

			CommandResult result;
			lock (this) {
				result = command_result;
				command_result = null;
			}

			command_mutex.ReleaseMutex ();
			if (result != null)
				return result;
			else
				return new CommandResult (CommandResultType.UnknownError);
		}

		void start_step_operation (StepOperation operation, StepFrame frame)
		{
			send_async_command (new Command (operation, frame));
		}

		void start_step_operation (StepOperation operation, TargetAddress until)
		{
			send_async_command (new Command (operation, until));
		}

		void start_step_operation (StepOperation operation)
		{
			send_async_command (new Command (operation));
		}

		void start_step_operation (StepMode mode)
		{
			start_step_operation (StepOperation.Native, get_simple_step_frame (mode));
		}

		void wait_for_completion ()
		{
			completed_event.WaitOne ();
			ready_event_handler ();
		}

		// <summary>
		//   Step one machine instruction.
		// </summary>
		public bool StepInstruction (bool synchronous)
		{
			if (!check_can_run ())
				return false;

			start_step_operation (StepOperation.StepInstruction);
			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public bool NextInstruction (bool synchronous)
		{
			if (!check_can_run ())
				return false;

			start_step_operation (StepOperation.NextInstruction);
			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public bool StepLine (bool synchronous)
		{
			if (!check_can_run ())
				return false;

			start_step_operation (StepOperation.StepLine, get_step_frame ());
			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public bool NextLine (bool synchronous)
		{
			if (!check_can_run ())
				return false;

			StepFrame frame = get_step_frame ();
			if (frame == null) {
				start_step_operation (StepMode.NextInstruction);
				goto done;
			}

			start_step_operation (
				StepOperation.NextLine,
				new StepFrame (frame.Start, frame.End, null, StepMode.Finish));

		done:
			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public bool Finish (bool synchronous)
		{
			if (!check_can_run ())
				return false;

			StackFrame frame = CurrentFrame;
			if (frame.Method == null) {
				command_mutex.ReleaseMutex ();
				throw new NoMethodException ();
			}

			start_step_operation (StepOperation.StepFrame, new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish));

			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		// <summary>
		//   Continue until reaching @until, hitting a breakpoint or receiving a
		//   signal.  This method just inserts a breakpoint at @until and resumes
		//   the target.
		// </summary>
		public bool Continue (TargetAddress until, bool synchronous)
		{
			return Continue (until, false, synchronous);
		}

		// <summary>
		//   Resume the target until a breakpoint is hit or it receives a signal.
		// </summary>
		public bool Continue (bool in_background, bool synchronous)
		{
			return Continue (TargetAddress.Null, in_background, synchronous);
		}

		public bool Continue (TargetAddress until, bool in_background, bool synchronous)
		{
			if (!check_can_run ())
				return false;

			if (in_background)
				start_step_operation (StepOperation.RunInBackground, until);
			else
				start_step_operation (StepOperation.Run, until);
			if (synchronous)
				wait_for_completion ();
			command_mutex.ReleaseMutex ();
			return true;
		}

		public void Stop ()
		{
			// Try to get the command mutex; if we succeed, then no stepping operation
			// is currently running.
			bool stopped = check_can_run ();
			if (stopped) {
				command_mutex.ReleaseMutex ();
				return;
			}

			// Ok, there's an operation running.  Stop the inferior and wait until the
			// currently running operation completed.
			inferior.Stop ();
			wait_for_completion ();
			ready_event_handler ();
		}

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		internal void AcquireThreadLock ()
		{
			// Try to get the command mutex; if we succeed, then no stepping operation
			// is currently running.
			bool stopped = check_can_run ();
			if (stopped)
				return;

			// Ok, there's an operation running.  Stop the inferior and wait until the
			// currently running operation completed.
			stop_requested = true;
			inferior.Stop ();
			stop_event.WaitOne ();
		}

		internal void ReleaseThreadLock ()
		{
			lock (this) {
				if (stop_requested) {
					stop_requested = false;
					completed_event.Reset ();
					restart_event.Set ();
				}
				command_mutex.ReleaseMutex ();
			}
		}

		Hashtable breakpoints = new Hashtable ();

		CommandResult insert_breakpoint (object data)
		{
			int index = inferior.InsertBreakpoint ((TargetAddress) data);
			return new CommandResult (CommandResultType.CommandOk, index);
		}

		CommandResult remove_breakpoint (object data)
		{
			inferior.RemoveBreakpoint ((int) data);
			return new CommandResult (CommandResultType.CommandOk);
		}

		// <summary>
		//   Insert a breakpoint at address @address.  Each time this breakpoint
		//   is hit, @handler will be called and @user_data will be passed to it
		//   as argument.  @needs_frame specifies whether the @handler needs the
		//   StackFrame argument.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		public int InsertBreakpoint (TargetAddress address, BreakpointHitHandler handler,
					     bool needs_frame, object user_data)
		{
			check_inferior ();

			CommandResult result = send_sync_command (new CommandFunc (insert_breakpoint), address);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();

			int index = (int) result.Data;
			breakpoints.Add (index, new BreakpointHandle (index, handler, needs_frame, user_data));
			return index;
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (inferior != null)
				send_sync_command (new CommandFunc (remove_breakpoint), index);
			breakpoints.Remove (index);
		}

		private struct BreakpointHandle
		{
			public readonly int Index;
			public readonly bool NeedsFrame;
			public readonly BreakpointHitHandler Handler;
			public readonly object UserData;

			public BreakpointHandle (int index, BreakpointHitHandler handler,
						 bool needs_frame, object user_data)
			{
				this.Index = index;
				this.Handler = handler;
				this.NeedsFrame = needs_frame;
				this.UserData = user_data;
			}
		}

		//
		// Disassembling.
		//

		private struct DisassembleData {
			public readonly TargetAddress Address;
			public readonly string Disassembly;

			public DisassembleData (TargetAddress address, string disasm)
			{
				this.Address = address;
				this.Disassembly = disasm;
			}
		}

		CommandResult disassemble_insn (object data)
		{
			try {
				TargetAddress address = (TargetAddress) data;
				string disasm = disassembler.DisassembleInstruction (ref address);
				DisassembleData result = new DisassembleData (address, disasm);
				return new CommandResult (CommandResultType.CommandOk, result);
			} catch (Exception e) {
				return new CommandResult (CommandResultType.Exception, e);
			}
		}

		public string DisassembleInstruction (ref TargetAddress address)
		{
			check_inferior ();
			CommandResult result = send_sync_command (new CommandFunc (disassemble_insn), address);
			if (result.Type == CommandResultType.CommandOk) {
				DisassembleData data = (DisassembleData) result.Data;
				address = data.Address;
				return data.Disassembly;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		//
		// Calling methods.
		//

		private struct CallMethodData
		{
			public readonly TargetAddress Method;
			public readonly long Argument;
			public readonly string StringArgument;

			public CallMethodData (TargetAddress method, long arg, string string_arg)
			{
				this.Method = method;
				this.Argument = arg;
				this.StringArgument = string_arg;
			}
		}

		CommandResult call_string_method (object data)
		{
			CallMethodData cdata = (CallMethodData) data;
			long retval = inferior.CallStringMethod (cdata.Method, cdata.Argument,
								 cdata.StringArgument);
			return new CommandResult (CommandResultType.CommandOk, retval);
		}

		internal long CallMethod (TargetAddress method, long method_argument,
					  string string_argument)
		{
			CallMethodData data = new CallMethodData (method, method_argument, string_argument);
			CommandResult result = send_sync_command (new CommandFunc (call_string_method), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();
			return (long) result.Data;
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

		//
		// ITargetMemoryAccess
		//

		private struct ReadMemoryData
		{
			public TargetAddress Address;
			public int Size;

			public ReadMemoryData (TargetAddress address, int size)
			{
				this.Address = address;
				this.Size = size;
			}
		}

		CommandResult do_read_memory (object data)
		{
			ReadMemoryData mdata = (ReadMemoryData) data;

			try {
				byte[] buffer = inferior.ReadBuffer (mdata.Address, mdata.Size);
				return new CommandResult (CommandResultType.CommandOk, buffer);
			} catch (Exception e) {
				return new CommandResult (CommandResultType.Exception, e);
			}
		}

		protected byte[] read_memory (TargetAddress address, int size)
		{
			ReadMemoryData data = new ReadMemoryData (address, size);
			CommandResult result = send_sync_command (new CommandFunc (do_read_memory), data);
			if (result.Type == CommandResultType.CommandOk)
				return (byte []) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		CommandResult do_read_string (object data)
		{
			ReadMemoryData mdata = (ReadMemoryData) data;

			try {
				string retval = inferior.ReadString (mdata.Address);
				return new CommandResult (CommandResultType.CommandOk, retval);
			} catch (Exception e) {
				return new CommandResult (CommandResultType.Exception, e);
			}
		}

		string read_string (TargetAddress address)
		{
			ReadMemoryData data = new ReadMemoryData (address, 0);
			CommandResult result = send_sync_command (new CommandFunc (do_read_string), data);
			if (result.Type == CommandResultType.CommandOk)
				return (string) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		ITargetMemoryReader get_memory_reader (TargetAddress address, int size)
		{
			byte[] buffer = read_memory (address, size);
			return new TargetReader (buffer, inferior);
		}

		AddressDomain ITargetMemoryInfo.AddressDomain {
			get {
				return inferior.AddressDomain;
			}
		}

		AddressDomain ITargetMemoryInfo.GlobalAddressDomain {
			get {
				return thread_manager.AddressDomain;
			}
		}

		byte ITargetMemoryAccess.ReadByte (TargetAddress address)
		{
			byte[] data = read_memory (address, 1);
			return data [0];
		}

		int ITargetMemoryAccess.ReadInteger (TargetAddress address)
		{
			ITargetMemoryReader reader = get_memory_reader (address, TargetIntegerSize);
			return reader.ReadInteger ();
		}

		long ITargetMemoryAccess.ReadLongInteger (TargetAddress address)
		{
			ITargetMemoryReader reader = get_memory_reader (address, TargetIntegerSize);
			return reader.ReadLongInteger ();
		}

		TargetAddress ITargetMemoryAccess.ReadAddress (TargetAddress address)
		{
			ITargetMemoryReader reader = get_memory_reader (address, TargetIntegerSize);
			return reader.ReadAddress ();
		}

		TargetAddress ITargetMemoryAccess.ReadGlobalAddress (TargetAddress address)
		{
			ITargetMemoryReader reader = get_memory_reader (address, TargetIntegerSize);
			return reader.ReadGlobalAddress ();
		}

		string ITargetMemoryAccess.ReadString (TargetAddress address)
		{
			return read_string (address);
		}

		ITargetMemoryReader ITargetMemoryAccess.ReadMemory (TargetAddress address, int size)
		{
			return get_memory_reader (address, size);
		}

		ITargetMemoryReader ITargetMemoryAccess.ReadMemory (byte[] buffer)
		{
			return new TargetReader (buffer, inferior);
		}

		byte[] ITargetMemoryAccess.ReadBuffer (TargetAddress address, int size)
		{
			return read_memory (address, size);
		}

		bool ITargetMemoryAccess.CanWrite {
			get { return false; }
		}

		void ITargetMemoryAccess.WriteBuffer (TargetAddress address, byte[] buffer, int size)
		{
			throw new InvalidOperationException ();
		}

		void ITargetMemoryAccess.WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetMemoryAccess.WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetMemoryAccess.WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetMemoryAccess.WriteAddress (TargetAddress address, TargetAddress value)
		{
			throw new InvalidOperationException ();
		}		

		//
		// Stack frames.
		//

		protected class MyStackFrame : StackFrame
		{
			SingleSteppingEngine sse;
			IInferiorStackFrame frame;
			MyBacktrace backtrace;

			Register[] registers;
			bool has_registers;

			public MyStackFrame (SingleSteppingEngine sse, TargetAddress address, int level,
					     IInferiorStackFrame frame, MyBacktrace backtrace,
					     SourceLocation source, IMethod method)
				: base (address, level, source, method)
			{
				this.sse = sse;
				this.frame = frame;
				this.backtrace = backtrace;
			}

			public MyStackFrame (SingleSteppingEngine sse, TargetAddress address, int level,
					     IInferiorStackFrame frame, MyBacktrace backtrace)
				: base (address, level)
			{
				this.sse = sse;
				this.frame = frame;
				this.backtrace = backtrace;
			}

			public override ITargetMemoryAccess TargetMemoryAccess {
				get { return sse; }
			}

			public override TargetAddress LocalsAddress {
				get { return frame.LocalsAddress; }
			}

			public override TargetAddress ParamsAddress {
				get { return frame.ParamsAddress; }
			}

			public override Register[] Registers {
				get {
					if (has_registers)
						return registers;

					if (backtrace == null) {
						registers = sse.GetRegisters ();
						has_registers = true;
					} else {
						registers = backtrace.UnwindStack (Level);
						has_registers = true;
					}

					return registers;
				}
			}
		}

		//
		// Backtrace.
		//

		protected class MyBacktrace : Backtrace
		{
			SingleSteppingEngine sse;
			IArchitecture arch;

			public MyBacktrace (SingleSteppingEngine sse, StackFrame[] frames)
				: base (frames)
			{
				this.sse = sse;
				arch = sse.Architecture;
			}

			public MyBacktrace (SingleSteppingEngine sse)
				: this (sse, null)
			{ }

			public void SetFrames (StackFrame[] frames)
			{
				this.frames = frames;
			}

			protected struct UnwindInfo {
				public readonly object Data;
				public readonly Register[] Registers;

				public UnwindInfo (object data, Register[] registers)
				{
					this.Data = data;
					this.Registers = registers;
				}
			}

			ArrayList unwind_info = null;

			protected UnwindInfo StartUnwindStack ()
			{
				if (unwind_info == null)
					unwind_info = new ArrayList ();
				if (unwind_info.Count > 0)
					return (UnwindInfo) unwind_info [0];

				Register[] registers = sse.GetRegisters ();
				object data = arch.UnwindStack (registers);

				UnwindInfo info = new UnwindInfo (data, registers);
				unwind_info.Add (info);
				return info;
			}

			protected UnwindInfo GetUnwindInfo (int level)
			{
				UnwindInfo start = StartUnwindStack ();
				if (level == 0)
					return start;
				else if (level < unwind_info.Count)
					return (UnwindInfo) unwind_info [level];

				level--;
				UnwindInfo last = GetUnwindInfo (level);
				StackFrame frame = frames [level];

				IMethod method = frame.Method;
				if ((method == null) || !method.IsLoaded || (last.Data == null)) {
					UnwindInfo new_info = new UnwindInfo (null, null);
					unwind_info.Add (new_info);
					return new_info;
				}

				int prologue_size;
				if (method.HasMethodBounds)
					prologue_size = (int) (method.MethodStartAddress - method.StartAddress);
				else
					prologue_size = (int) (method.EndAddress - method.StartAddress);
				int offset = (int) (frame.TargetAddress - method.StartAddress);
				prologue_size = Math.Min (prologue_size, offset);
				prologue_size = Math.Min (prologue_size, arch.MaxPrologueSize);

				byte[] prologue = sse.read_memory (method.StartAddress, prologue_size);
				Console.WriteLine ("UNWIND STACK: {0} {1} {2} {3} - {4}", level,
						   method.Name, method.StartAddress, prologue_size,
						   TargetBinaryReader.HexDump (prologue));

				object new_data;
				Register[] regs = arch.UnwindStack (prologue, sse, last.Data, out new_data);

				UnwindInfo info = new UnwindInfo (new_data, regs);
				unwind_info.Add (info);
				return info;
			}

			public Register[] UnwindStack (int level)
			{
				UnwindInfo info = GetUnwindInfo (level);
				return info.Registers;
			}
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
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					if (engine_thread != null) {
						engine_thread.Abort ();
						engine_thread = null;
					}
					if (thread_notify != null) {
						thread_notify.Dispose ();
						thread_notify = null;
					}
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

		~SingleSteppingEngine ()
		{
			Dispose (false);
		}
	}
}
