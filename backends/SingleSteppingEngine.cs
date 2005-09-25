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
using System.Runtime.Serialization;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Architecture;
using Mono.Debugger.Remoting;

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

	public class CommandAttribute : Attribute
	{ }

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
	internal class SingleSteppingEngine : MarshalByRefObject
	{
		// <summary>
		//   This is invoked after compiling a trampoline - it returns whether or
		//   not we should enter that trampoline.
		// </summary>
		internal delegate bool TrampolineHandler (IMethod method);

		protected SingleSteppingEngine (ThreadManager manager, Inferior inferior)
		{
			this.manager = manager;
			this.inferior = inferior;
			this.start = inferior.ProcessStart;

			inferior.TargetOutput += new TargetOutputHandler (inferior_output_handler);
			inferior.DebuggerOutput += new DebuggerOutputHandler (debugger_output_handler);
			inferior.DebuggerError += new DebuggerErrorHandler (debugger_error_handler);

			PID = inferior.PID;

			engine_stopped_event = new ManualResetEvent (false);
		}

		public SingleSteppingEngine (ThreadManager manager, ProcessStart start)
			: this (manager, Inferior.CreateInferior (manager, start))
		{
			inferior.Run (true);
			PID = inferior.PID;

			is_main = true;

			setup_engine ();

			process = DebuggerManager.CreateProcess (this);
			ID = process.ID;
			Name = process.Name;
			target_access = new ServerTargetAccess (this);
		}

		public SingleSteppingEngine (ThreadManager manager, Inferior inferior, int pid)
			: this (manager, inferior)
		{
			this.PID = pid;
			inferior.Attach (pid);

			is_main = false;
			TID = inferior.TID;

			setup_engine ();

			process = DebuggerManager.CreateProcess (this);
			ID = process.ID;
			Name = process.Name;
			target_access = new ServerTargetAccess (this);
		}

		void setup_engine ()
		{
			Report.Debug (DebugFlags.Threads, "New SSE ({0}): {1}",
				      DebuggerWaitHandle.CurrentThread, this);

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			disassembler.SymbolTable = inferior.DebuggerBackend.SymbolTableManager.SimpleSymbolTable;
			current_simple_symtab = inferior.DebuggerBackend.SymbolTableManager.SimpleSymbolTable;
			current_symtab = inferior.DebuggerBackend.SymbolTableManager.SymbolTable;

			inferior.DebuggerBackend.SymbolTableManager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);

			exception_handlers = new Hashtable ();
		}

#region child event processing
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
			if (has_thread_lock) {
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} while being thread-locked ({2})",
					      this, cevent, stop_event);
				if (stop_event != null)
					throw new InternalError ();
				stop_event = cevent;
				stopped = true;
				return;
			}
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION)
				Report.Debug (DebugFlags.Notification,
					      "{0} received event {1} {2} ({3:x})",
					      this, cevent, (NotificationType) cevent.Argument,
					      status);
			else
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} ({2:x}) at {3} while running {4}",
					      this, cevent, status, inferior.CurrentFrame,
					      current_operation);

			if (manager.HandleChildEvent (this, inferior, ref cevent))
				return;
			ProcessChildEvent (cevent);
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
		protected void ProcessChildEvent (Inferior.ChildEvent cevent)
		{
			Inferior.ChildEventType message = cevent.Type;
			int arg = (int) cevent.Argument;

			bool exiting = false;

			TargetEventArgs result = null;

			if ((message == Inferior.ChildEventType.THROW_EXCEPTION) ||
			    (message == Inferior.ChildEventType.HANDLE_EXCEPTION)) {
				TargetAddress stack = new TargetAddress (
					inferior.AddressDomain, cevent.Data1);
				TargetAddress ip = new TargetAddress (
					manager.AddressDomain, cevent.Data2);

				Report.Debug (DebugFlags.EventLoop,
					      "{0} received exception: {1} {2} {3}",
					      this, message, stack, ip);

				bool stop_on_exc;
				if (message == Inferior.ChildEventType.THROW_EXCEPTION)
					stop_on_exc = throw_exception (stack, ip);
				else
					stop_on_exc = handle_exception (stack, ip);

				Report.Debug (DebugFlags.SSE,
					      "{0} {1}stopping at exception ({2}:{3}) - {4} - {5}",
					      this, stop_on_exc ? "" : "not ", stack, ip,
					      current_operation, temp_breakpoint_id);

				if (stop_on_exc) {
					PushOperation (new OperationException (ip, stack, false));
					return;
				}

				do_continue ();
				return;
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
				if ((message == Inferior.ChildEventType.CHILD_EXITED) ||
				    (message == Inferior.ChildEventType.CHILD_SIGNALED))
					// we can't remove the breakpoint anymore after
					// the target exited, but we need to clear this id.
					temp_breakpoint_id = 0;
				else if ((message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) &&
					 (arg == temp_breakpoint_id)) {
					// we hit the temporary breakpoint; this'll always
					// happen in the `correct' thread since the
					// `temp_breakpoint_id' is only set in this
					// SingleSteppingEngine and not in any other thread's.

					inferior.RemoveBreakpoint (temp_breakpoint_id);
					temp_breakpoint_id = 0;

					Breakpoint bpt = manager.BreakpointManager.LookupBreakpoint (arg);
					Report.Debug (DebugFlags.SSE,
						      "{0} hit temporary breakpoint {1} at {2} {3}",
						      this, arg, inferior.CurrentFrame, bpt);
					if (bpt == null) {
						message = Inferior.ChildEventType.CHILD_STOPPED;
						arg = 0;
						cevent = new Inferior.ChildEvent (
							Inferior.ChildEventType.CHILD_STOPPED, 0, 0, 0);
					} else
						goto send_result;
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
				} else if (step_over_breakpoint (TargetAddress.Null, TargetAddress.Null, false)) {
					Report.Debug (DebugFlags.SSE,
						      "{0} now stepping over breakpoint", this);
					return;
				} else if (!child_breakpoint (arg)) {
					// we hit any breakpoint, but its handler told us
					// to resume the target and continue.
					do_continue ();
					return;
				}
			}

			if (temp_breakpoint_id != 0) {
				Report.Debug (DebugFlags.SSE,
					      "{0} hit temporary breakpoint at {1}: {2}",
					      this, inferior.CurrentFrame, message);

				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}

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

				PushOperation (new OperationException (ip, exc, true));
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
			}

		send_result:
			// If `result' is not null, then the target stopped abnormally.
			if (result != null) {
				// Ok, inform the user that we stopped.
				step_operation_finished ();
				operation_completed (result);
				if (is_main && !reached_main && !exiting) {
					arch = inferior.Architecture;
					reached_main = true;

					SimpleStackFrame ret_frame = arch.UnwindStack (
						inferior, get_simple_frame (), null, null);
					if (ret_frame != null)
						main_method_retaddr = ret_frame.Address;

					manager.ReachedMain ();
				}
				return;
			}

			//
			// Sometimes, we need to do just one atomic operation - in all
			// other cases, `current_operation' is the current stepping
			// operation.
			//
			// ProcessEvent() will either start another atomic operation
			// (and return false) or tell us the stepping operation is
			// completed by returning true.
			//

			if (current_operation != null) {
				bool send_result;

				if (!current_operation.ProcessEvent (this, inferior, cevent,
								     out result, out send_result))
					return;

				if (result != null)
					goto send_result;

				if (!send_result) {
					step_operation_finished ();
					operation_completed (null);
					return;
				}
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
				Report.Debug (DebugFlags.SSE,
					      "{0} frame changed at {1} => new operation {2}",
					      this, frame, new_operation, message);

				if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT)
					new_operation.PendingBreakpoint = arg;

				ProcessOperation (new_operation);
				return;
			}

			//
			// Now we're really finished.
			//
			int pending_bpt = -1;
			if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT)
				pending_bpt = arg;
			else if (current_operation != null)
				pending_bpt = current_operation.PendingBreakpoint;
			step_operation_finished ();
			if (pending_bpt >= 0) {
				Breakpoint bpt = manager.BreakpointManager.LookupBreakpoint (pending_bpt);
				if (bpt != null) {
					result = new TargetEventArgs (
						TargetEventType.TargetHitBreakpoint, bpt.Index,
						current_frame);
					goto send_result;
				}
			}

			result = new TargetEventArgs (TargetEventType.TargetStopped, 0, current_frame);
			goto send_result;
		}
#endregion

		void operation_completed (TargetEventArgs result)
		{
			lock (this) {
				engine_stopped = true;
				engine_stopped_event.Set ();
				if (result != null)
					manager.SendTargetEvent (this, result);
				process.SendTargetEvent (result, true);
			}
		}

		internal void Start (TargetAddress func, bool is_main)
		{
			if (is_main) {
				if (!func.IsNull)
					insert_temporary_breakpoint (func);
				current_operation = new OperationInitialize ();
				this.is_main = true;
				do_continue ();
			} else {
				TargetEventArgs args = new TargetEventArgs (TargetEventType.TargetRunning);
				manager.SendTargetEvent (this, args);
				process.SendTargetEvent (args, false);
				current_operation = new OperationRun (TargetAddress.Null, true);
				do_continue ();
			}
		}


		void set_registers (Registers registers)
		{
			if (!registers.FromCurrentFrame)
				throw new InvalidOperationException ();

			this.registers = registers;
			inferior.SetRegisters (registers);
		}

		// <summary>
		//   Start a new stepping operation.
		//
		//   All stepping operations are done asynchronously.
		//
		//   The inferior basically just knows two kinds of stepping operations:
		//   there is do_continue() to continue execution (until a breakpoint is
		//   hit or the target receives a signal or exits) and there is do_step_native()
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
		void StartOperation ()
		{
			lock (this) {
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					throw new TargetException (TargetError.NotStopped);
				}

				engine_stopped = false;
				engine_stopped_event.Reset ();
			}
		}

		void ProcessOperation (Operation operation)
		{
			stop_requested = false;

			Report.Debug (DebugFlags.SSE,
				      "{0} starting {1}", this, operation);

			current_operation = operation;
			operation.Execute (this);
		}

		void PushOperation (Operation operation)
		{
			current_operation.PushOperation (this, operation);
			operation.Execute (this);
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
			inferior.DebuggerBackend.UpdateSymbolTable (inferior);

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

#region public properties
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

		internal ITargetAccess TargetAccess {
			get { return target_access; }
		}

		internal Inferior Inferior {
			get { return inferior; }
		}

		public IArchitecture Architecture {
			get { return arch; }
		}

		public Process Process {
			get { return process; }
		}

		public ThreadManager ThreadManager {
			get { return manager; }
		}

		public DebuggerManager DebuggerManager {
			get { return manager.DebuggerBackend.DebuggerManager; }
		}

		public Backtrace CurrentBacktrace {
			get { return current_backtrace; }
		}

		public StackFrame CurrentFrame {
			get { return current_frame; }
		}

		public IMethod CurrentMethod {
			get { return current_method; }
		}

		public TargetAddress CurrentFrameAddress {
			get { return inferior.CurrentFrame; }
		}
#endregion

		public ITargetMemoryInfo TargetMemoryInfo {
			get {
				check_inferior ();
				return inferior.TargetMemoryInfo;
			}
		}

		public ITargetInfo TargetInfo {
			get {
				check_inferior ();
				return inferior.TargetInfo;
			}
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_inferior ();
			return inferior.GetMemoryMaps ();
		}

		public void Kill ()
		{
			lock (this) {
				if (!engine_stopped)
					operation_completed (
						new TargetEventArgs (TargetEventType.TargetExited, 0));
			}

			if (inferior != null)
				inferior.Kill ();
		}

		public void Stop ()
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

				if (current_operation is OperationStepOverBreakpoint) {
					int index = ((OperationStepOverBreakpoint) current_operation).Index;

					Report.Debug (DebugFlags.SSE,
						      "{0} stepped over breakpoint {1}: {2}",
						      this, index, inferior.CurrentFrame);

					inferior.EnableBreakpoint (index);
					manager.ReleaseGlobalThreadLock (this);
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
					Console.WriteLine ("Couldn't get the current stack frame from inferior: {0}", ex);
				}
			}
		}

		protected void check_inferior ()
		{
			if (inferior == null)
				throw new TargetException (TargetError.NoTarget);
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

			if (!bpt.CheckBreakpointHit (target_access, inferior.CurrentFrame))
				return false;

			return true;
		}

		bool step_over_breakpoint (TargetAddress trampoline, TargetAddress until, bool current)
		{
			int index;
			bool is_enabled;
			Breakpoint bpt = manager.BreakpointManager.LookupBreakpoint (
				inferior.CurrentFrame, out index, out is_enabled);

			if ((index == 0) || !is_enabled ||
			    (!current && (bpt != null) && bpt.Breaks (process.ID)))
				return false;

			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over {3}breakpoint {1} at {2} until {4}/{5}",
				      this, index, inferior.CurrentFrame,
				      current ? "current " : "", trampoline, until);

			PushOperation (new OperationStepOverBreakpoint (index, trampoline, until));
			return true;
		}

		bool throw_exception (TargetAddress info, TargetAddress ip)
		{
			TargetAddress exc = inferior.ReadAddress (info + inferior.TargetAddressSize);

			Report.Debug (DebugFlags.SSE,
				      "{0} throwing exception {1} at {2} while running {3}", this, exc, ip,
				      current_operation);

			if ((current_operation != null) && !current_operation.StartFrame.IsNull &&
			    current_operation.StartFrame == ip)
				return false;

			if (current_operation is OperationRuntimeInvoke)
				return false;

			foreach (Breakpoint bpt in exception_handlers.Values) {
				Report.Debug (DebugFlags.SSE,
					      "{0} invoking exception handler {1} for {0}",
					      this, bpt, exc);

				if (!bpt.CheckBreakpointHit (target_access, exc))
					continue;

				Report.Debug (DebugFlags.SSE,
					      "{0} stopped on exception {1} at {2}", this, exc, ip);

				inferior.WriteInteger (info + 2 * inferior.TargetAddressSize, 1);
				return true;
			}

			return false;
		}

		bool handle_exception (TargetAddress info, TargetAddress ip)
		{
			TargetAddress stack = inferior.ReadAddress (info);
			TargetAddress exc = inferior.ReadAddress (info + inferior.TargetAddressSize);

			Report.Debug (DebugFlags.SSE,
				      "{0} handling exception {1} at {2} while running {3}", this, exc, ip,
				      current_operation);

			if (current_operation == null)
				return true;

			return current_operation.HandleException (this, stack, exc);
		}

		SimpleStackFrame get_simple_frame ()
		{
			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();

			registers = inferior.GetRegisters ();
			return new SimpleStackFrame (iframe, registers, 0);
		}

		// <summary>
		//   Compute the StackFrame for target address @address.
		// </summary>
		StackFrame get_frame ()
		{
			SimpleStackFrame simple = get_simple_frame ();
			TargetAddress address = simple.Address;

			// If we have a current_method and the address is still inside
			// that method, we don't need to do a method lookup.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			current_frame = StackFrame.CreateFrame (
				process, target_access, simple, current_method);

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

			bool same_method = false;

			// Only do a method lookup if we actually need it.
			if ((current_method != null) &&
			    MethodBase.IsInSameMethod (current_method, address))
				same_method = true;
			else
				current_method = Lookup (address);

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();
			registers = inferior.GetRegisters ();

			// Compute the current stack frame.
			if ((current_method != null) && current_method.HasSource) {
				SourceAddress source = current_method.Source.Lookup (address);

				if (!same_method) {
					// If check_method_operation() returns true, it already
					// started a stepping operation, so the target is
					// currently running.
					Operation new_operation = check_method_operation (
						address, current_method, source, operation);
					if (new_operation != null)
						return new_operation;
				}

				SimpleStackFrame simple = new SimpleStackFrame (
					iframe, registers, 0);
				current_frame = StackFrame.CreateFrame (
					process, target_access, simple, current_method, source);
			} else {
				if (!same_method && (current_method != null)) {
					Operation new_operation = check_method_operation (
						address, current_method, null, operation);
					if (new_operation != null)
						return new_operation;
				}

				SimpleStackFrame simple = new SimpleStackFrame (
					iframe, registers, 0);
				current_frame = StackFrame.CreateFrame (
					process, target_access, simple, current_symtab,
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
			// Do nothing if this is not a source stepping operation.
			if ((operation == null) || !operation.IsSourceOperation)
				return null;

			if (method.WrapperType != WrapperType.None)
				return new OperationWrapper (method);

			ILanguageBackend language = method.Module.LanguageBackend;
			if (source == null)
				return null;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				// We stopped between two source lines.  This normally
				// happens when returning from a method call; in this
				// case, we need to continue stepping until we reach the
				// next source line.
				return new OperationStep (new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					null, language, StepMode.Finish));
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				// Do not stop inside a method's prologue code, but stop
				// immediately behind it (on the first instruction of the
				// method's actual code).
				return new OperationStep (new StepFrame (
					method.StartAddress, method.MethodStartAddress, null,
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

			Report.Debug (DebugFlags.SSE, "{0} inserted temp breakpoint {1}:{2} at {3}",
				      this, temp_breakpoint_id, dr_index, address);
		}

		// <summary>
		//   Step over the next machine instruction.
		// </summary>
		void do_next_native ()
		{
			check_inferior ();
			frames_invalid ();
			TargetAddress address = inferior.CurrentFrame;

			// Check whether this is a call instruction.
			int insn_size;
			TargetAddress call = arch.GetCallTarget (inferior, address, out insn_size);

			Report.Debug (DebugFlags.SSE, "{0} do_next_native: {1} {2}", this,
				      address, call);

			// Step one instruction unless this is a call
			if (call.IsNull) {
				do_step_native ();
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

			if (step_over_breakpoint (trampoline, until, true))
				return;

			inferior.Continue ();
		}

		void do_step_native ()
		{
			if (step_over_breakpoint (TargetAddress.Null, TargetAddress.Null, true))
				return;

			inferior.Step ();
		}

		void step_operation_finished ()
		{
			current_operation = null;
		}

		void do_trampoline (ILanguageBackend language, TargetAddress trampoline,
				    TrampolineHandler handler, bool is_start)
		{
			TargetAddress compile = language.CompileMethodFunc;

			Report.Debug (DebugFlags.SSE,
				      "{0} found trampoline {1}/{3} (compile is {2}) while running {4}",
				      this, trampoline, compile, is_start, current_operation);

			if (is_start) {
				Console.WriteLine ("DO TRAMPOLINE: {0}", trampoline);
				PushOperation (new OperationFinish (true));
				// do_continue (trampoline);
				return;
			}

			if (compile.IsNull) {
				IMethod method = null;
				if (current_symtab != null) {
					method = Lookup (trampoline);
				}
				if (!MethodHasSource (method)) {
					do_next_native ();
					return;
				}

				do_continue (trampoline);
				return;
			}

			PushOperation (new OperationCompileMethod (compile, trampoline, handler));
			return;
		}

		protected static bool MethodHasSource (IMethod method)
		{
			if (method == null)
				return false;

			if (!method.HasSource || !method.Module.StepInto)
				return false;

			MethodSource source = method.Source;
			if ((source == null) || source.IsDynamic)
				return false;

			SourceFileFactory factory = method.Module.DebuggerBackend.SourceFileFactory;
			if (!factory.Exists (source.SourceFile.FileName))
				return false;

			if (!method.HasMethodBounds)
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
		//   Create a step frame to step until the next source line.
		// </summary>
		StepFrame CreateStepFrame ()
		{
			check_inferior ();
			StackFrame frame = current_frame;
			ILanguageBackend language = (frame.Method != null) ? frame.Method.Module.LanguageBackend : null;

			if (frame.SourceAddress == null)
				return new StepFrame (language, StepMode.SingleInstruction);

			// The current source line started at the current address minus
			// SourceOffset; the next source line will start at the current
			// address plus SourceRange.

			int offset = frame.SourceAddress.SourceOffset;
			int range = frame.SourceAddress.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, frame.SimpleFrame, language, StepMode.StepFrame);
		}

		// <summary>
		//   Create a step frame for a native stepping operation.
		// </summary>
		StepFrame CreateStepFrame (StepMode mode)
		{
			check_inferior ();
			ILanguageBackend language = (current_method != null) ? current_method.Module.LanguageBackend : null;

			return new StepFrame (language, mode);
		}

		StackData save_stack ()
		{
			//
			// Save current state.
			//
			StackData stack_data = new StackData (
				current_method, inferior.CurrentFrame, current_frame,
				current_backtrace, registers);

			current_method = null;
			current_frame = null;
			current_backtrace = null;
			registers = null;

			return stack_data;
		}

		void restore_stack (StackData stack)
		{
			if (inferior.CurrentFrame != stack.Address) {
				Report.Debug (DebugFlags.SSE,
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
			Report.Debug (DebugFlags.SSE,
				      "{0} restored stack: {1}", this, current_frame);
		}

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		public bool AcquireThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} acquiring thread lock", this);

			has_thread_lock = true;
			stopped = inferior.Stop (out stop_event);
			Inferior.StackFrame frame = inferior.GetCurrentFrame ();

			Report.Debug (DebugFlags.Threads,
				      "{0} acquired thread lock: {1} {2} {3} {4}",
				      this, stopped, stop_event, EndStackAddress,
				      frame.StackPointer);

			if (!EndStackAddress.IsNull)
				inferior.WriteAddress (EndStackAddress, frame.StackPointer);

			return stop_event != null;
		}

		public void ReleaseThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} releasing thread lock: {1} {2}",
				      this, stopped, stop_event);

			has_thread_lock = false;

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

				if (manager.HandleChildEvent (this, inferior, ref cevent))
					return;
				ProcessChildEvent (cevent);
			}
		}

		void inferior_output_handler (bool is_stderr, string line)
		{
			process.OnInferiorOutput (is_stderr, line);
		}

		void debugger_output_handler (string line)
		{
			process.OnDebuggerOutput (line);
		}

		void debugger_error_handler (object sender, string message, Exception e)
		{
			process.OnDebuggerError (sender, message, e);
		}

		public override string ToString ()
		{
			return String.Format ("SSE ({0}:{1}:{2:x})", ID, PID, TID);
		}

#region SSE Commands

		[Command]
		public void StepInstruction ()
		{
			StartOperation ();
			ProcessOperation (new OperationStep (StepMode.SingleInstruction));
		}

		[Command]
		public void StepNativeInstruction ()
		{
			StartOperation ();
			ProcessOperation (new OperationStep (StepMode.NativeInstruction));
		}

		[Command]
		public void NextInstruction ()
		{
			StartOperation ();
			ProcessOperation (new OperationStep (StepMode.NextInstruction));
		}

		[Command]
		public void StepLine ()
		{
			StartOperation ();
			ProcessOperation (new OperationStep (StepMode.SourceLine));
		}

		[Command]
		public void NextLine ()
		{
			StartOperation ();
			ProcessOperation (new OperationStep (StepMode.NextLine));
		}

		[Command]
		public void Finish ()
		{
			StartOperation ();
			ProcessOperation (new OperationFinish (false));
		}

		[Command]
		public void FinishNative ()
		{
			StartOperation ();
			ProcessOperation (new OperationFinish (true));
		}

		[Command]
		public void Continue (TargetAddress until, bool in_background)

		{
			StartOperation ();
			ProcessOperation (new OperationRun (until, in_background));
		}

		[Command]
		public void RuntimeInvoke (TargetFunctionType method_argument,
					   TargetObject object_argument,
					   TargetObject[] param_objects)
		{
			StartOperation ();
			ProcessOperation (new OperationRuntimeInvoke (
				method_argument, object_argument, param_objects));
		}

		[Command]
		public void RuntimeInvoke (TargetFunctionType method_argument,
					   TargetObject object_argument,
					   TargetObject[] param_objects,
					   CommandResult result)
		{
			StartOperation ();
			ProcessOperation (new OperationRuntimeInvoke (
				method_argument, object_argument, param_objects, result));
		}

		[Command]
		public void CallMethod (TargetAddress method, long method_argument,
					string string_argument, CommandResult result)
		{
			StartOperation ();
			ProcessOperation (new OperationCallMethod (
				method, method_argument, string_argument, result));
		}

		[Command]
		public void CallMethod (TargetAddress method, TargetAddress arg1,
					TargetAddress arg2, CommandResult result)
		{
			StartOperation ();
			ProcessOperation (new OperationCallMethod (
				method, arg1.Address, arg2.Address, result));
		}

		[Command]
		public Backtrace GetBacktrace (int max_frames)
		{
			inferior.DebuggerBackend.UpdateSymbolTable (inferior);

			if (current_frame == null)
				throw new TargetException (TargetError.NoStack);

			current_backtrace = new Backtrace (
				process, arch, current_frame, main_method_retaddr, max_frames);

			current_backtrace.GetBacktrace (
				target_access, arch, current_symtab, current_simple_symtab);

			return current_backtrace;
		}

		[Command]
		public Registers GetRegisters ()
		{
			registers = inferior.GetRegisters ();
			return registers;
		}

		[Command]
		public void SetRegisters (Registers registers)
		{
			if (!registers.FromCurrentFrame)
				throw new InvalidOperationException ();

			this.registers = registers;
			inferior.SetRegisters (registers);
		}

		[Command]
		public int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
		{
			return manager.BreakpointManager.InsertBreakpoint (
				inferior, breakpoint, address);
		}

		[Command]
		public void RemoveBreakpoint (int index)
		{
			manager.BreakpointManager.RemoveBreakpoint (inferior, index);
		}

		[Command]
		public void InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func,
					      CommandResult result)
		{
			StartOperation ();
			ProcessOperation (new OperationInsertBreakpoint (breakpoint, func, result));
		}

		[Command]
		public int AddEventHandler (EventType type, Breakpoint breakpoint)
		{
			if (type != EventType.CatchException)
				throw new InternalError ();

			int id = ++next_exception_handler_id;
			exception_handlers.Add (id, breakpoint);
			return id;
		}

		[Command]
		public void RemoveEventHandler (int index)
		{
			exception_handlers.Remove (index);
		}

		[Command]
		public int GetInstructionSize (TargetAddress address)
		{
			lock (disassembler) {
				return disassembler.GetInstructionSize (address);
			}
		}

		[Command]
		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			lock (disassembler) {
				return disassembler.DisassembleInstruction (method, address);
			}
		}

		[Command]
		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			lock (disassembler) {
				return disassembler.DisassembleMethod (method);
			}
		}

		[Command]
		public byte[] ReadMemory (TargetAddress address, int size)
		{
			return inferior.ReadBuffer (address, size);
		}

		[Command]
		public byte ReadByte (TargetAddress address)
		{
			return inferior.ReadByte (address);
		}

		[Command]
		public int ReadInteger (TargetAddress address)
		{
			return inferior.ReadInteger (address);
		}

		[Command]
		public long ReadLongInteger (TargetAddress address)
		{
			return inferior.ReadLongInteger (address);
		}

		[Command]
		public TargetAddress ReadAddress (TargetAddress address)
		{
			return inferior.ReadAddress (address);
		}

		[Command]
		public TargetAddress ReadGlobalAddress (TargetAddress address)
		{
			return inferior.ReadGlobalAddress (address);
		}

		[Command]
		public string ReadString (TargetAddress address)
		{
			return inferior.ReadString (address);
		}

		[Command]
		public void WriteMemory (TargetAddress address, byte[] buffer)
		{
			inferior.WriteBuffer (address, buffer);
		}

		[Command]
		public string PrintObject (Style style, TargetObject obj)
		{
			return style.FormatObject (target_access, obj);
		}

		[Command]
		public string PrintType (Style style, TargetType type)
		{
			return style.FormatType (target_access, type);
		}

		[Command]
		public object Invoke (TargetAccessDelegate func, object data)
		{
			return func (target_access, data);
		}

#endregion

#region IDisposable implementation
		~SingleSteppingEngine ()
		{
			Dispose (false);
		}

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
				if (inferior != null)
					inferior.Dispose ();
				inferior = null;

				process.Dispose ();;
			}

			disposed = true;
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion

		protected IMethod current_method;
		protected StackFrame current_frame;
		protected Backtrace current_backtrace;
		protected Registers registers;

		bool stopped;
		Inferior.ChildEvent stop_event;
		Operation current_operation;

		ThreadManager manager;
		Process process;
		Inferior inferior;
		TargetAccess target_access;
		IArchitecture arch;
		IDisassembler disassembler;
		ProcessStart start;
		ISymbolTable current_symtab;
		ISimpleSymbolTable current_simple_symtab;
		Hashtable exception_handlers;
		bool engine_stopped;
		ManualResetEvent engine_stopped_event;
		bool stop_requested;
		bool has_thread_lock;
		bool is_main, reached_main;
		public readonly string Name;
		public readonly int ID;
		public readonly int PID;
		public readonly int TID;

		static int next_exception_handler_id = 0;

		int stepping_over_breakpoint;

		internal TargetAddress EndStackAddress;

		TargetAddress main_method_retaddr = TargetAddress.Null;
		TargetState target_state = TargetState.NO_TARGET;

#region Nested SSE classes
		internal sealed class StackData
		{
			public readonly IMethod Method;
			public readonly TargetAddress Address;
			public readonly StackFrame Frame;
			public readonly Backtrace Backtrace;
			public readonly Registers Registers;

			public StackData (IMethod method, TargetAddress address,
					  StackFrame frame, Backtrace backtrace,
					  Registers registers)
			{
				this.Method = method;
				this.Address = address;
				this.Frame = frame;
				this.Backtrace = backtrace;
				this.Registers = registers;
			}
		}
#endregion

#region SSE Operations
	protected abstract class Operation {
		protected enum EventResult
		{
			Running,
			Completed,
			CompletedCallback,
			AskParent
		}

		public abstract bool IsSourceOperation {
			get;
		}

		public TargetAddress StartFrame;
		public int PendingBreakpoint = -1;

		public virtual void Execute (SingleSteppingEngine sse)
		{
			StartFrame = sse.inferior.CurrentFrame;
			DoExecute (sse);
		}

		protected abstract void DoExecute (SingleSteppingEngine sse);

		Operation child;

		public virtual void PushOperation (SingleSteppingEngine sse, Operation op)
		{
			if (child != null)
				child.PushOperation (sse, op);
			else
				child = op;
		}

		public virtual bool ProcessEvent (SingleSteppingEngine sse,
						  Inferior inferior,
						  Inferior.ChildEvent cevent,
						  out TargetEventArgs args, out bool send_result)
		{
			EventResult result = ProcessEvent (sse, inferior, cevent, out args);

			switch (result) {
			case EventResult.Running:
				send_result = false;
				return false;

			case EventResult.Completed:
				send_result = true;
				return true;

			case EventResult.CompletedCallback:
				send_result = false;
				return true;

			default:
				throw new InternalError ("FUCK: {0} {1}", this, result);
			}
		}

		protected virtual EventResult ProcessEvent (SingleSteppingEngine sse,
							    Inferior inferior,
							    Inferior.ChildEvent cevent,
							    out TargetEventArgs args)
		{
			if (child != null) {
				EventResult result = child.ProcessEvent (
					sse, inferior, cevent, out args);

				if (result != EventResult.AskParent)
					return result;

				Operation old_child = child;
				child = null;

				Report.Debug (DebugFlags.EventLoop,
					      "{0} resending event {1} from {2} to {3}",
					      sse, cevent, old_child, this);
			}

			return DoProcessEvent (sse, inferior, cevent, out args);
		}

		protected abstract EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args);

		public virtual bool HandleException (SingleSteppingEngine sse,
						     TargetAddress stack, TargetAddress exc)
		{
			return true;
		}

		protected virtual string MyToString ()
		{
			return "";
		}

		public override string ToString ()
		{
			if (child == null)
				return String.Format ("{0} ()", GetType ().Name, MyToString ());
			else
				return String.Format ("{0}:{1}", GetType ().Name, child);
		}
	}

	protected class OperationInitialize : Operation
	{
		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{ }

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} initialize ({1})", this,
				      DebuggerWaitHandle.CurrentThread);

			sse.manager.Initialize (inferior);
			Report.Debug (DebugFlags.SSE, "{0} initialize done", sse);

			args = null;
			return EventResult.Completed;
		}
	}

	protected class OperationStepOverBreakpoint : Operation
	{
		TargetAddress trampoline;
		TargetAddress until;
		public readonly int Index;
		bool has_thread_lock;

		public OperationStepOverBreakpoint (int index, TargetAddress trampoline,
						    TargetAddress until)
		{
			this.Index = index;
			this.trampoline = trampoline;
			this.until = until;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			sse.manager.AcquireGlobalThreadLock (sse);
			sse.inferior.DisableBreakpoint (Index);

			has_thread_lock = true;

			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over breakpoint {1} until {2}/{3} ({4})",
				      sse, Index, trampoline, until, sse.current_method);

			if (!trampoline.IsNull) {
				until = TargetAddress.Null;
				sse.insert_temporary_breakpoint (trampoline);
				sse.inferior.Continue ();
				return;
			}

			IMethod method = sse.Lookup (sse.inferior.CurrentFrame);

			if (method == null) {
				sse.inferior.Step ();
				return;
			}

			ILanguageBackend language = method.Module.LanguageBackend;

			int insn_size;
			TargetAddress current_frame = sse.inferior.CurrentFrame;
			TargetAddress call = sse.arch.GetCallTarget (
				sse.inferior, current_frame, out insn_size);
			if (call.IsNull) {
				sse.inferior.Step ();
				return;
			}

			bool is_start;
			TargetAddress trampoline_address = language.GetTrampolineAddress (
				sse.inferior, call, out is_start);

			if (!trampoline_address.IsNull)
				sse.do_trampoline (language, trampoline_address, null, is_start);
			else
				sse.inferior.Step ();
		}

		void ReleaseThreadLock (SingleSteppingEngine sse)
		{
			if (has_thread_lock) {
				Report.Debug (DebugFlags.SSE,
					      "{0} releasing thread lock at {1}",
					      sse, sse.inferior.CurrentFrame);

				sse.inferior.EnableBreakpoint (Index);
				sse.manager.ReleaseGlobalThreadLock (sse);

				has_thread_lock = false;
			}
		}

		public override void PushOperation (SingleSteppingEngine sse, Operation op)
		{
			// ReleaseThreadLock (sse);
			base.PushOperation (sse, op);
		}

		protected override EventResult ProcessEvent (SingleSteppingEngine sse,
							     Inferior inferior,
							     Inferior.ChildEvent cevent,
							     out TargetEventArgs args)
		{
			if (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK)
				ReleaseThreadLock (sse);
			return base.ProcessEvent (sse, inferior, cevent, out args);
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} stepped over breakpoint {1} at {2}: {3} {4}",
				      sse, Index, inferior.CurrentFrame, cevent, until);

			if ((cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) &&
			    (cevent.Argument != Index)) {
				args = null;
				return EventResult.Completed;
			}

			if (!until.IsNull) {
				sse.do_continue (until);

				args = null;
				until = TargetAddress.Null;
				return EventResult.Running;
			}

			args = null;
			return EventResult.AskParent;
		}
	}

	protected abstract class OperationStepBase : Operation
	{
		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			args = null;
			if (DoProcessEvent (sse, inferior))
				return EventResult.Completed;
			else
				return EventResult.Running;
		}

		protected abstract bool DoProcessEvent (SingleSteppingEngine sse,
							Inferior inferior);

		protected bool CheckTrampoline (SingleSteppingEngine sse, TargetAddress call)
		{
			foreach (ILanguageBackend language in sse.inferior.DebuggerBackend.Languages) {
				bool is_start;
				TargetAddress trampoline = language.GetTrampolineAddress (
					sse.inferior, call, out is_start);

				/*
				 * If this is a trampoline, insert a breakpoint at the start of
				 * the corresponding method and continue.
				 *
				 * We don't need to distinguish between StepMode.SingleInstruction
				 * and StepMode.StepFrame here since we'd leave the step frame anyways
				 * when entering the method.
				 */
				if (!trampoline.IsNull) {
					sse.do_trampoline (
						language, trampoline, TrampolineHandler, is_start);
					return true;
				}
			}

			return false;
		}

		protected abstract bool TrampolineHandler (IMethod method);
	}

	protected class OperationStep : OperationStepBase
	{
		public StepMode StepMode;
		public StepFrame StepFrame;

		public OperationStep (StepMode mode)
		{
			this.StepMode = mode;
		}

		public OperationStep (StepFrame frame)
		{
			this.StepFrame = frame;
			this.StepMode = frame.Mode;
		}

		public override bool IsSourceOperation {
			get {
				return (StepMode == StepMode.SourceLine) ||
					(StepMode == StepMode.NextLine);
			}
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			switch (StepMode) {
			case StepMode.NativeInstruction:
				sse.do_step_native ();
				break;

			case StepMode.NextInstruction:
				sse.do_next_native ();
				break;

			case StepMode.SourceLine:
				StepFrame = sse.CreateStepFrame ();
				if (StepFrame == null)
					sse.do_step_native ();
				else
					Step (sse, true);
				break;

			case StepMode.NextLine:
				// We cannot just set a breakpoint on the next line
				// since we do not know which way the program's
				// control flow will go; ie. there may be a jump
				// instruction before reaching the next line.
				StepFrame frame = sse.CreateStepFrame ();
				if (frame == null)
					sse.do_next_native ();
				else {
					StepFrame = new StepFrame (
						frame.Start, frame.End, frame.StackFrame,
						null, StepMode.Finish);
					Step (sse, true);
				}
				break;

			case StepMode.SingleInstruction:
				StepFrame = sse.CreateStepFrame (StepMode.SingleInstruction);
				Step (sse, true);
				break;

			case StepMode.Finish:
				Step (sse, true);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		public override bool HandleException (SingleSteppingEngine sse,
						      TargetAddress stack, TargetAddress exc)
		{
			if ((StepMode != StepMode.SourceLine) && (StepMode != StepMode.NextLine) &&
			    (StepMode != StepMode.StepFrame))
				return true;

			/*
			 * If we don't have a StepFrame or if the StepFrame doesn't have a
			 * SimpleStackFrame, we're doing something like instruction stepping -
			 * always stop in this case.
			 */
			if ((StepFrame == null) || (StepFrame.StackFrame == null))
				return true;

			SimpleStackFrame oframe = StepFrame.StackFrame;

			Report.Debug (DebugFlags.SSE,
				      "{0} handling exception: {1} {2} - {3} {4} - {5}", sse,
				      StepFrame, oframe, stack, oframe.StackPointer,
				      stack < oframe.StackPointer);

			if (stack < oframe.StackPointer)
				return false;

			return true;
		}

		protected override bool TrampolineHandler (IMethod method)
		{
			if (method == null)
				return false;

			if (method.WrapperType == WrapperType.DelegateInvoke)
				return true;

			if (StepMode == StepMode.SourceLine)
				return SingleSteppingEngine.MethodHasSource (method);

			return true;
		}

		protected bool Step (SingleSteppingEngine sse, bool first)
		{
			if (StepFrame == null)
				return true;

			TargetAddress current_frame = sse.inferior.CurrentFrame;
			bool in_frame = sse.is_in_step_frame (StepFrame, current_frame);
			Report.Debug (DebugFlags.SSE, "{0} stepping at {1} in {2} ({3}in frame)",
				      sse, current_frame, StepFrame, !in_frame ? "not " : "");
			if (!first && !in_frame)
				return true;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the specified step frame.
			 */
			int insn_size;
			TargetAddress call = sse.arch.GetCallTarget (
				sse.inferior, current_frame, out insn_size);
			if (call.IsNull) {
				sse.do_step_native ();
				return false;
			}

			if ((sse.current_method != null) && (sse.current_method.HasMethodBounds) &&
			    (call >= sse.current_method.MethodStartAddress) &&
			    (call < sse.current_method.MethodEndAddress)) {
				/* Intra-method call (we stay outside the prologue/epilogue code, so this also
				 * can't be a recursive call). */
				sse.do_step_native ();
				return false;
			}

			/*
			 * In StepMode.Finish, always step over all methods.
			 */
			if ((StepMode == StepMode.Finish) || (StepMode == StepMode.NextLine)) {
				sse.do_next_native ();
				return false;
			}

			/*
			 * If we have a source language, check for trampolines.
			 * This will trigger a JIT compilation if neccessary.
			 */
			if (CheckTrampoline (sse, call))
				return false;

			/*
			 * When StepMode.SingleInstruction was requested, enter the method
			 * no matter whether it's a system function or not.
			 */
			if (StepMode == StepMode.SingleInstruction) {
				sse.do_step_native ();
				return false;
			}

			/*
			 * Try to find out whether this is a system function by doing a symbol lookup.
			 * If it can't be found in the symbol tables, assume it's a system function
			 * and step over it.
			 */
			IMethod method = sse.Lookup (call);

			/*
			 * If this is a PInvoke/icall wrapper, check whether we want to step into
			 * the wrapped function.
			 */
			if ((method != null) && (method.WrapperType != WrapperType.None)) {
				if (method.WrapperType == WrapperType.DelegateInvoke) {
					sse.do_step_native ();
					return false;
				}
			}

			if (!SingleSteppingEngine.MethodHasSource (method)) {
				sse.do_next_native ();
				return false;
			}

			/*
			 * Finally, step into the method.
			 */
			sse.do_step_native ();
			return false;
		}

		protected override bool DoProcessEvent (SingleSteppingEngine sse,
							Inferior inferior)
		{
			Report.Debug (DebugFlags.SSE, "{0} processing {1} event.",
				      sse, this);
			return Step (sse, false);
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", StepMode, StepFrame);
		}
	}

	protected class OperationRun : Operation
	{
		TargetAddress until;
		bool in_background;

		public OperationRun (TargetAddress until, bool in_background)
		{
			this.until = until;
			this.in_background = in_background;
		}

		public bool InBackground {
			get { return in_background; }
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			if (!until.IsNull)
				sse.do_continue (until);
			else
				sse.do_continue ();
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			args = null;
			if (!until.IsNull && inferior.CurrentFrame == until)
				return EventResult.Completed;
			Report.Debug (DebugFlags.EventLoop, "{0} received {1} at {2} in {3}",
				      sse, cevent, inferior.CurrentFrame, this);
			if (cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT)
				return EventResult.Completed;
			Execute (sse);
			return EventResult.Running;
		}

		public override bool HandleException (SingleSteppingEngine sse,
						      TargetAddress stack, TargetAddress exc)
		{
			return false;
		}
	}

	protected class OperationFinish : OperationStepBase
	{
		public readonly bool Native;

		public OperationFinish (bool native)
		{
			this.Native = native;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		StepFrame step_frame;
		TargetAddress until;

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			if (!Native) {
				StackFrame frame = sse.CurrentFrame;
				if (frame.Method == null)
					throw new TargetException (TargetError.NoMethod);

				step_frame = new StepFrame (
					frame.Method.StartAddress, frame.Method.EndAddress,
					frame.SimpleFrame, null, StepMode.Finish);
			} else {
				Inferior.StackFrame frame = sse.inferior.GetCurrentFrame ();
				until = frame.StackPointer;

				Report.Debug (DebugFlags.SSE,
					      "{0} starting finish native until {1}",
					      sse, until);
			}

			sse.do_step_native ();
		}

		protected override bool DoProcessEvent (SingleSteppingEngine sse,
							Inferior inferior)
		{
			if (step_frame != null) {
				bool in_frame = sse.is_in_step_frame (step_frame, inferior.CurrentFrame);
				Report.Debug (DebugFlags.SSE,
					      "{0} finish {1} at {2} ({3}", sse, step_frame,
					      inferior.CurrentFrame, in_frame);

				if (!in_frame)
					return true;

				sse.do_next_native ();
				return false;
			}

			Inferior.StackFrame frame = inferior.GetCurrentFrame ();
			TargetAddress stack = frame.StackPointer;

			Report.Debug (DebugFlags.SSE,
				      "{0} finish native: stack = {1}, " +
				      "until = {2}", sse, stack, until);

			if (stack <= until) {
				sse.do_next_native ();
				return false;
			}

			return true;
		}

		protected override bool TrampolineHandler (IMethod method)
		{
			return false;
		}
	}

	protected abstract class OperationCallback : Operation
	{
		public readonly long ID = ++next_id;
		StackData stack_data;

		static int next_id = 0;

		public override void Execute (SingleSteppingEngine sse)
		{
			stack_data = sse.save_stack ();
			base.Execute (sse);
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.EventLoop,
				      "{0} received event {1} at {2} while waiting for " +
				      "callback {3}", sse, cevent, inferior.CurrentFrame, this);

			args = null;
			if (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK) {
				Report.Debug (DebugFlags.SSE, "{0} aborting callback {1} at {2}: {3}",
					      sse, this, inferior.CurrentFrame, cevent);
				RestoreStack (sse);
				return EventResult.CompletedCallback;
			}

			if (ID != cevent.Argument) {
				Abort ();
				goto out_frame_changed;
			}

			if (!CallbackCompleted (sse, inferior, cevent.Data1, cevent.Data2))
				return EventResult.Running;

			if (stack_data != null) {
				sse.restore_stack (stack_data);
				return EventResult.CompletedCallback;
			}

		out_frame_changed:
			sse.frame_changed (inferior.CurrentFrame, null);
			args = new TargetEventArgs (
				TargetEventType.TargetStopped, 0, sse.current_frame);
			return EventResult.Completed;
		}

		protected abstract bool CallbackCompleted (SingleSteppingEngine sse,
							   Inferior inferior,
							   long data1, long data2);

		public override bool IsSourceOperation {
			get { return false; }
		}

		public void Abort ()
		{
			if (stack_data == null)
				return;

			if (stack_data.Frame != null)
				stack_data.Frame.Dispose ();

			if (stack_data.Backtrace != null)
				stack_data.Backtrace.Dispose ();
		}

		protected void RestoreStack (SingleSteppingEngine sse)
		{
			if (stack_data != null)
				sse.restore_stack (stack_data);
		}
	}

	protected class OperationRuntimeInvoke : OperationCallback
	{
		public readonly MonoFunctionType MethodArgument;
		public readonly TargetObject ObjectArgument;
		public readonly TargetObject[] ParamObjects;
		public readonly bool Debug;
		public readonly CommandResult Result;

		MonoLanguageBackend language;
		TargetAddress method, method_argument;
		bool method_compiled;

		public override bool IsSourceOperation {
			get { return true; }
		}

		public OperationRuntimeInvoke (TargetFunctionType method_argument,
					       TargetObject object_argument,
					       TargetObject[] param_objects)
		{
			this.MethodArgument = (MonoFunctionType) method_argument;
			this.ObjectArgument = (TargetObject) object_argument;
			this.ParamObjects = new TargetObject [param_objects.Length];
			param_objects.CopyTo (this.ParamObjects, 0);
			this.Debug = true;

			this.method = TargetAddress.Null;
			this.method_argument = TargetAddress.Null;
		}

		public OperationRuntimeInvoke (TargetFunctionType method_argument,
					       TargetObject object_argument,
					       TargetObject[] param_objects,
					       CommandResult result)
			: this (method_argument, object_argument, param_objects)
		{
			this.Result = result;
			this.Debug = false;
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			language = sse.ThreadManager.DebuggerBackend.MonoLanguage;
			method_argument = MethodArgument.GetMethodAddress (sse.TargetAccess);

			if ((ObjectArgument != null) && ObjectArgument.Location.HasAddress) {
				sse.inferior.CallMethod (
					language.GetVirtualMethodFunc,
					ObjectArgument.Location.Address.Address,
					method_argument.Address, ID);
			} else {
				method = method_argument;
				sse.inferior.CallMethod (
					language.CompileMethodFunc, method.Address, 0, ID);
			}
		}

		protected override bool CallbackCompleted (SingleSteppingEngine sse,
							   Inferior inferior,
							   long data1, long data2)
		{
			if (method.IsNull) {
				method = new TargetAddress (inferior.AddressDomain, data1);

				inferior.CallMethod (language.CompileMethodFunc,
						     method.Address, 0, ID);
				return false;
			}

			if (!method_compiled) {
				method_compiled = true;

				TargetAddress invoke = new TargetAddress (
					inferior.AddressDomain, data1);

				Report.Debug (DebugFlags.SSE,
					      "Runtime invoke: {0}", invoke);

				if (Debug)
					sse.insert_temporary_breakpoint (invoke);

				inferior.RuntimeInvoke (
					language.RuntimeInvokeFunc,
					method, ObjectArgument,
					ParamObjects, ID, Debug);
				return false;
			}

			Report.Debug (DebugFlags.SSE,
				      "Runtime invoke done: {0:x} {1:x}",
				      data1, data2);

			RuntimeInvokeResult result = new RuntimeInvokeResult ();
			if (data2 != 0) {
				TargetAddress exc_address = new TargetAddress (
					inferior.AddressDomain, data2);
				TargetFundamentalObject exc_obj = (TargetFundamentalObject)
					language.CreateObject (sse.target_access, exc_address);

				result.ExceptionMessage = (string) exc_obj.GetObject (sse.target_access);
			}

			if (data1 != 0) {
				TargetAddress retval_address = new TargetAddress (
					inferior.AddressDomain, data1);

				result.ReturnObject = language.CreateObject (
					sse.target_access, retval_address);
			}

			Result.Result = result;

			return true;
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if (Debug)
				return base.DoProcessEvent (sse, inferior, cevent, out args);

			if (cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
				Report.Debug (DebugFlags.SSE,
					      "{0} hit breakpoint {1} at {2} during runtime-invoke",
					      sse, cevent.Argument, inferior.CurrentFrame);
				sse.do_continue ();
				args = null;
				return EventResult.Running;
			} else if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
				   (cevent.Argument == 0)) {
				Report.Debug (DebugFlags.SSE,
					      "{0} stopped at {1} during runtime-invoke",
					      sse, inferior.CurrentFrame);
				sse.do_continue ();
				args = null;
				return EventResult.Running;
			}

			return base.DoProcessEvent (sse, inferior, cevent, out args);
		}

		public override bool HandleException (SingleSteppingEngine sse,
						      TargetAddress stack, TargetAddress exc)
		{
			return false;
		}
	}

	protected class OperationCallMethod : OperationCallback
	{
		public readonly CallMethodType Type;
		public readonly TargetAddress Method;
		public readonly long Argument1;
		public readonly long Argument2;
		public readonly string StringArgument;
		public readonly CommandResult Result;

		public OperationCallMethod (TargetAddress method, long arg, string sarg,
					    CommandResult result)
		{
			this.Type = CallMethodType.LongString;
			this.Method = method;
			this.Argument1 = arg;
			this.StringArgument = sarg;
			this.Result = result;
		}

		public OperationCallMethod (TargetAddress method, long arg1, long arg2,
					    CommandResult result)
		{
			this.Type = CallMethodType.LongLong;
			this.Method = method;
			this.Argument1 = arg1;
			this.Argument2 = arg2;
			this.Result = result;
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			switch (Type) {
			case CallMethodType.LongLong:
				sse.inferior.CallMethod (Method, Argument1, Argument2, ID);
				break;

			case CallMethodType.LongString:
				sse.inferior.CallMethod (Method, Argument1, StringArgument, ID);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		protected override bool CallbackCompleted (SingleSteppingEngine sse,
							   Inferior inferior,
							   long data1, long data2)
		{
			if (inferior.TargetAddressSize == 4)
				data1 &= 0xffffffffL;

			Result.Result = new TargetAddress (inferior.AddressDomain, data1);

			Report.Debug (DebugFlags.SSE,
				      "{0} call method done: {1:x} {2:x}",
				      sse, data1, data2);
				
			return true;
		}
	}

	protected class OperationCompileMethod : OperationCallback
	{
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress MethodAddress;
		public readonly TrampolineHandler TrampolineHandler;

		bool completed;

		public OperationCompileMethod (TargetAddress compile, TargetAddress address,
					       TrampolineHandler handler)
		{
			this.CompileMethod = compile;
			this.MethodAddress = address;
			this.TrampolineHandler = handler;
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			Report.Debug (DebugFlags.SSE, "{0} compiling method: {1}", sse, MethodAddress);
			sse.inferior.CallMethod (CompileMethod, MethodAddress.Address, 0, ID);
		}

		protected override bool CallbackCompleted (SingleSteppingEngine sse,
							   Inferior inferior,
							   long data1, long data2)
		{
			TargetAddress address = new TargetAddress (
				inferior.GlobalAddressDomain, data1);

			Report.Debug (DebugFlags.SSE, "{0} done compiling method: {1}",
				      sse, address);

			RestoreStack (sse);
			completed = true;

			IMethod method = sse.Lookup (address);
			Report.Debug (DebugFlags.SSE,
				      "{0} compiled method: {1} {2} {3} {4} {5}",
				      sse, address, method,
				      method != null ? method.Module : null,
				      SingleSteppingEngine.MethodHasSource (method),
				      TrampolineHandler);

			if ((TrampolineHandler != null) && !TrampolineHandler (method)) {
				sse.do_next_native ();
				return false;
			}

			Report.Debug (DebugFlags.SSE, "{0} entering trampoline: {1}",
				      sse, address);

			sse.do_continue (address, address);
			return false;
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if (!completed)
				return base.DoProcessEvent (sse, inferior, cevent, out args);


			args = null;
			return EventResult.AskParent;
		}
	}

	protected class OperationInsertBreakpoint : OperationCallback
	{
		public readonly Breakpoint Breakpoint;
		public readonly MonoFunctionType Function;
		public readonly CommandResult Result;

		MonoLanguageBackend language;
		TargetAddress method, address;

		public OperationInsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func,
						  CommandResult result)
		{
			this.Breakpoint = breakpoint;
			this.Function = (MonoFunctionType) func;
			this.Result = result;
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			if (language == null)
				language = sse.ThreadManager.DebuggerBackend.MonoLanguage;

			method = Function.GetMethodAddress (sse.target_access);

			sse.inferior.CallMethod (language.CompileMethodFunc,
						 method.Address, 0, ID);
		}

		protected override bool CallbackCompleted (SingleSteppingEngine sse,
							   Inferior inferior,
							   long data1, long data2)
		{
			address = new TargetAddress (inferior.AddressDomain, data1);

			int index = sse.ThreadManager.BreakpointManager.InsertBreakpoint (
				inferior, Breakpoint, address);

			Result.Result = index;
			return true;
		}
	}

	protected class OperationException : Operation
	{
		TargetAddress ip;
		TargetAddress exc;
		bool unhandled;

		public OperationException (TargetAddress ip, TargetAddress exc, bool unhandled)
		{
			this.ip = ip;
			this.exc = exc;
			this.unhandled = unhandled;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			sse.do_continue (ip);
		}

		protected override EventResult DoProcessEvent (SingleSteppingEngine sse,
							       Inferior inferior,
							       Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if (unhandled) {
				sse.frame_changed (inferior.CurrentFrame, null);
				args = new TargetEventArgs (
					TargetEventType.UnhandledException,
					exc, sse.current_frame);
				return EventResult.Completed;
			} else {
				sse.frame_changed (inferior.CurrentFrame, null);
				args = new TargetEventArgs (
					TargetEventType.Exception,
					exc, sse.current_frame);
				return EventResult.Completed;
			}
		}
	}

	protected class OperationWrapper : OperationStepBase
	{
		IMethod method;

		public OperationWrapper (IMethod method)
		{
			this.method = method;
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute (SingleSteppingEngine sse)
		{
			sse.do_step_native ();
		}

		protected override bool DoProcessEvent (SingleSteppingEngine sse, Inferior inferior)
		{
			TargetAddress current_frame = inferior.CurrentFrame;
			Report.Debug (DebugFlags.SSE, "{0} wrapper stopped at {1} ({2}:{3})",
				      sse, current_frame, method.StartAddress, method.EndAddress);
			if ((current_frame < method.StartAddress) || (current_frame > method.EndAddress))
				return true;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the current method.
			 */
			int insn_size;
			TargetAddress call = sse.arch.GetCallTarget (
				sse.inferior, current_frame, out insn_size);
			if (!call.IsNull && CheckTrampoline (sse, call))
				return false;

			sse.do_step_native ();
			return false;
		}

		protected override bool TrampolineHandler (IMethod method)
		{
			if (method == null)
				return false;

			if (method.WrapperType == WrapperType.DelegateInvoke)
				return true;

			return SingleSteppingEngine.MethodHasSource (method);
		}
	}
#endregion
	}

	[Serializable]
	internal enum CommandType {
		Message,
		TargetAccess
	}

	[Serializable]
	internal class Command {
		public SingleSteppingEngine Engine;
		public readonly CommandType Type;
		public object Data1, Data2;
		public object Result;

		public Command (IMethodCallMessage message, IMessageSink sink)
		{
			this.Type = CommandType.Message;
			this.Data1 = message;
			this.Data2 = sink;
		}

		public Command (SingleSteppingEngine sse, TargetAccessDelegate func, object data)
		{
			this.Type = CommandType.TargetAccess;
			this.Engine = sse;
			this.Data1 = func;
			this.Data2 = data;
		}

		public override string ToString ()
		{
			return String.Format ("Command ({0}:{1}:{2}:{3})",
					      Engine, Type, Data1, Data2);
		}
	}

	[Serializable]
	internal enum CallMethodType
	{
		LongLong,
		LongString
	}

	[Serializable]
	internal class RuntimeInvokeResult
	{
		public TargetObject ReturnObject;
		public string ExceptionMessage;
	}

	internal sealed class CommandResult : MarshalByRefObject
	{
		public object Result;
	}
}
