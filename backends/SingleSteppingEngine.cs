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
	// </summary>
	public class SingleSteppingEngine
	{
		public SingleSteppingEngine (DebuggerBackend backend, Process process,
					     IInferior inferior, bool native)
		{
			this.backend = backend;
			this.process = process;
			this.symtab_manager = backend.SymbolTableManager;
			this.inferior = inferior;
			this.native = native;

			inferior.SingleSteppingEngine = this;
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			symtab_manager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);

			step_event = new AutoResetEvent (false);
			start_event = new ManualResetEvent (false);
			thread_notify = new ThreadNotify (new ReadyEventHandler (ready_event_handler));
		}

		void ready_event_handler ()
		{
			lock (this) {
				if (command_result == null)
					return;

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

		public void Run ()
		{
			if (engine_thread != null)
				throw new AlreadyHaveTargetException ();

			engine_thread = new Thread (new ThreadStart (start_engine_thread));
			engine_thread.Start ();

			wait_until_engine_is_ready ();
		}

		public void Attach (int pid)
		{
			if (engine_thread != null)
				throw new AlreadyHaveTargetException ();

			this.pid = pid;

			engine_thread = new Thread (new ThreadStart (start_engine_thread_attach));
			engine_thread.Start ();

			wait_until_engine_is_ready ();
		}

		void wait_until_engine_is_ready ()
		{
			while (!start_event.WaitOne ())
				;

			ready_event_handler ();
		}

		void send_result (ChildEventType message, int arg)
		{
			lock (this) {
				command_result = new CommandResult (message, arg);
				thread_notify.Signal ();
				result_sent = true;
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

		void engine_thread_main (Command command)
		{
			bool first = true;

			do {
				try {
					process_command (command);
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0}", e);
				}

				if (first) {
					engine_ready ();
					first = false;
				}

				while (!step_event.WaitOne ())
					;

				lock (this) {
					command = current_command;
					current_command = null;
				}
			} while (true);
		}

		bool wait ()
		{
		again:
			ChildEvent child_event = inferior.Wait ();

			if (child_event == null)
				goto again;

			ChildEventType message = child_event.Type;
			int arg = child_event.Argument;

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
						// we hit the temporary breakpoint.
						message = ChildEventType.CHILD_STOPPED;
						arg = 0;
					} else if (!child_breakpoint (arg)) {
						// we hit any breakpoint, but its handler told us
						// to resume the target and continue.
						do_continue_nowait (true);
						goto again;
					}
				}

				if (temp_breakpoint_id != 0) {
					inferior.RemoveBreakpoint (temp_breakpoint_id);
					temp_breakpoint_id = 0;
				}
			}

			switch (message) {
			case ChildEventType.CHILD_STOPPED:
				if (arg != 0) {
					if (!backend.SignalHandler (process, arg)) {
						do_continue_nowait (false);
						goto again;
					}
					frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
					send_result (message, arg);
					return false;
				}

				return true;

			case ChildEventType.CHILD_HIT_BREAKPOINT:
				Console.WriteLine ("BREAKPOINT: {0}", arg);
				if (!child_breakpoint (arg)) {
					do_continue_nowait (true);
					goto again;
				}
				return true;

			default:
				send_result (message, arg);
				return false;
			}
		}

		void process_command (Command command)
		{
			bool ok;

			if (command == null)
				return;

		again:
			switch (command.Operation) {
			case StepOperation.Run:
				TargetAddress until = command.Until;
				if (!until.IsNull)
					insert_temporary_breakpoint (until);
				ok = do_continue (false);
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

			if (!ok)
				return;

			if (initialized && !reached_main) {
				main_method_retaddr = inferior.GetReturnAddress ();
				backend.ReachedMain (process);
				reached_main = true;
			}

			TargetAddress frame = inferior.CurrentFrame;

			// After returning from `main', resume the target and keep
			// running until it exits (or hit a breakpoint or receives
			// a signal).
			if (!main_method_retaddr.IsNull && (frame == main_method_retaddr)) {
				ok = do_continue (false);
				return;
			}

			Command new_command = frame_changed (frame, 0, command.Operation);
			if (new_command != null) {
				command = new_command;
				goto again;
			}
			send_result (ChildEventType.CHILD_STOPPED, 0);
		}

		void update_symtabs (object sender, ISymbolTable symbol_table)
		{
#if FALSE
			disassembler.SymbolTable = symbol_table;
			current_symtab = symbol_table;
			if (State == TargetState.STOPPED) {
				frames_invalid ();
				current_method = null;
				frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
			}
#endif
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
				check_stopped ();
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
		public StackFrame[] GetBacktrace ()
		{
			check_stopped ();

			if (current_backtrace != null)
				return current_backtrace;

			backend.UpdateSymbolTable ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (-1, main_method_retaddr);
			current_backtrace = new StackFrame [frames.Length];

			for (int i = 0; i < frames.Length; i++) {
				TargetAddress address = frames [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceLocation source = method.Source.Lookup (address);
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i], i, source, method);
				} else
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i], i);
			}

			return current_backtrace;
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
				check_stopped ();
				if (current_method == null)
					throw new NoMethodException ();
				return current_method;
			}
		}

		IInferior inferior;
		IArchitecture arch;
		DebuggerBackend backend;
		Process process;
		IDisassembler disassembler;
		SymbolTableManager symtab_manager;
		ISymbolTable current_symtab;
		Thread engine_thread;
		ManualResetEvent start_event;
		AutoResetEvent step_event;
		ThreadNotify thread_notify;
		bool result_sent = false;
		bool native;
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

		void check_disposed ()
		{ }

		void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		void check_stopped ()
		{
			check_inferior ();

			if ((State != TargetState.STOPPED) && (State != TargetState.CORE_FILE))
				throw new TargetNotStoppedException ();
		}

		void check_can_run ()
		{
			check_inferior ();

			if (in_event || (State != TargetState.STOPPED))
				throw new TargetNotStoppedException ();
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
		bool debugger_info_read;

		private enum StepOperation {
			None,
			Native,
			Run,
			StepInstruction,
			NextInstruction,
			StepLine,
			NextLine,
			StepFrame
		}

		private class Command {
			public StepOperation Operation;
			public StepFrame StepFrame;
			public TargetAddress Until;

			public Command (StepOperation operation, StepFrame frame)
			{
				this.Operation = operation;
				this.StepFrame = frame;
				this.Until = TargetAddress.Null;
			}

			public Command (StepOperation operation, TargetAddress until)
			{
				this.Operation = operation;
				this.StepFrame = null;
				this.Until = until;
			}

			public Command (StepOperation operation)
			{
				this.Operation = operation;
				this.StepFrame = null;
				this.Until = TargetAddress.Null;
			}
		}

		private class CommandResult {
			public ChildEventType EventType;
			public int Argument;

			public CommandResult (ChildEventType type, int arg)
			{
				this.EventType = type;
				this.Argument = arg;
			}
		}

		// <remarks>
		//   These two variables are shared between the two threads, so you need to
		//   lock (this) before accessing/modifying them.
		// </remarks>
		Command current_command = null;
		CommandResult command_result = null;

		bool must_continue = false;

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
			if (breakpoint == 0)
				return backend.BreakpointHit (inferior.CurrentFrame);

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

		IMethod current_method;
		StackFrame current_frame;
		StackFrame[] current_backtrace;

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
				ILanguageBackend language = current_method.Module.Language;

				current_frame = new StackFrame (
					inferior, address, frames [0], 0, source, current_method);
			} else
				current_frame = new StackFrame (
					inferior, address, frames [0], 0);

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
			IMethod old_method = current_method;

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
				ILanguageBackend language = current_method.Module.Language;

				// If check_method_operation() returns true, it already
				// started a stepping operation, so the target is
				// currently running.
				Command new_command = check_method_operation (
					address, current_method, source, operation);
				if (new_command != null)
					return new_command;

				current_frame = new StackFrame (
					inferior, address, frames [0], 0, source, current_method);
			} else
				current_frame = new StackFrame (
					inferior, address, frames [0], 0);

			// If the method changed, notify our clients.
			if (current_method != old_method) {
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
			ILanguageBackend language = method.Module.Language;
			if (language == null)
				return null;

			// Do nothing if this is not a source stepping operation.
			if ((operation != StepOperation.StepLine) &&
			    (operation != StepOperation.NextLine) &&
			    (operation != StepOperation.Run))
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
				foreach (StackFrame frame in current_backtrace)
					frame.Dispose ();
				current_backtrace = null;
			}
		}

		int temp_breakpoint_id = 0;
		void insert_temporary_breakpoint (TargetAddress address)
		{
			check_inferior ();
			temp_breakpoint_id = inferior.InsertHardwareBreakpoint (address, 0);
		}

		// <summary>
		//   Single-step one machine instruction.
		// </summary>
		bool do_step ()
		{
			check_inferior ();

			inferior.Step ();

			return wait ();
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
			return do_continue (false);
		}

		// <summary>
		//   Resume the target.  If @is_breakpoint is true, the current
		//   instruction is a breakpoint; in this case we need to step one
		//   instruction before we can resume the target (see `must_continue' in
		//   child_event() for more info).
		// </summary>
		bool do_continue (bool is_breakpoint)
		{
			if (!do_continue_nowait (is_breakpoint))
				return false;

			return wait ();
		}

		bool do_continue_nowait (bool is_breakpoint)
		{
			check_inferior ();

			if (is_breakpoint || inferior.CurrentInstructionIsBreakpoint) {
				must_continue = true;
				inferior.Step ();
				if (!wait ())
					return false;
			}

			inferior.EnableAllBreakpoints ();
			inferior.Continue ();
			return true;
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
					TargetAddress trampoline = frame.Language.GetTrampoline (call);

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
						return do_continue (false);
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
				if (current_symtab != null)
					method = current_symtab.Lookup (call);
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
		//   Continue until reaching @until, hitting a breakpoint or receiving a
		//   signal.  This method just inserts a breakpoint at @until and resumes
		//   the target.
		// </summary>
		public void Continue (TargetAddress until)
		{
			check_inferior ();
			start_step_operation (StepOperation.Run);
		}

		// <summary>
		//   Resume the target until a breakpoint is hit or it receives a signal.
		// </summary>
		public void Continue ()
		{
			check_inferior ();
			start_step_operation (StepOperation.Run, TargetAddress.Null);
		}

		// <summary>
		//   Create a step frame to step until the next source line.
		// </summary>
		StepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ?
				frame.Method.Module.Language : null;

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
			ILanguageBackend language;

			if (frame != null)
				language = (frame.Method != null) ? frame.Method.Module.Language : null;
			else
				language = null;

			return new StepFrame (language, mode);
		}

		void start_step_operation (StepOperation operation, StepFrame frame)
		{
			lock (this) {
				change_target_state (TargetState.RUNNING, 0);
				current_command = new Command (operation, frame);
				step_event.Set ();
			}
		}

		void start_step_operation (StepOperation operation, TargetAddress until)
		{
			lock (this) {
				change_target_state (TargetState.RUNNING, 0);
				current_command = new Command (operation, until);
				step_event.Set ();
			}
		}

		void start_step_operation (StepOperation operation)
		{
			lock (this) {
				change_target_state (TargetState.RUNNING, 0);
				current_command = new Command (operation);
				step_event.Set ();
			}
		}

		void start_step_operation (StepMode mode)
		{
			start_step_operation (StepOperation.Native, get_simple_step_frame (mode));
		}

		// <summary>
		//   Step one machine instruction.
		// </summary>
		public void StepInstruction ()
		{
			check_can_run ();
			start_step_operation (StepOperation.StepInstruction);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public void NextInstruction ()
		{
			check_can_run ();
			start_step_operation (StepOperation.NextInstruction);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public void StepLine ()
		{
			check_can_run ();
			start_step_operation (StepOperation.StepLine, get_step_frame ());
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public void NextLine ()
		{
			check_can_run ();
			StepFrame frame = get_step_frame ();
			if (frame == null) {
				start_step_operation (StepMode.NextInstruction);
				return;
			}

			start_step_operation (
				StepOperation.NextLine,
				new StepFrame (frame.Start, frame.End, null, StepMode.Finish));
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public void Finish ()
		{
			check_can_run ();
			StackFrame frame = CurrentFrame;
			if (frame.Method == null)
				throw new NoMethodException ();

			start_step_operation (StepOperation.StepFrame, new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish));
		}

		Hashtable breakpoints = new Hashtable ();

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
			int index = inferior.InsertBreakpoint (address);
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
				inferior.RemoveBreakpoint (index);
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
	}
}
