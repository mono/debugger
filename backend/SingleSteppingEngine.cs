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

using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
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
	//   See the `Thread' class for the "user interface".
	// </summary>
	internal class SingleSteppingEngine : ThreadServant
	{
		// <summary>
		//   This is invoked after compiling a trampoline - it returns whether or
		//   not we should enter that trampoline.
		// </summary>
		internal delegate bool TrampolineHandler (Method method);
		internal delegate bool CheckBreakpointHandler ();

		protected SingleSteppingEngine (ThreadManager manager, ProcessServant process,
						ProcessStart start)
			: base (manager, process)
		{
			Report.Debug (DebugFlags.Threads, "New SSE ({0}): {1}",
				      DebuggerWaitHandle.CurrentThread, this);

			exception_handlers = new Hashtable ();
		}

		public SingleSteppingEngine (ThreadManager manager, ProcessServant process,
					     ProcessStart start, out CommandResult result)
			: this (manager, process, start)
		{
			inferior = Inferior.CreateInferior (manager, process, start);

			is_main = true;
			if (start.PID != 0) {
				this.pid = start.PID;
				inferior.Attach (pid);
			} else {
				pid = inferior.Run ();
			}

			result = new ThreadCommandResult (thread);
			current_operation = new OperationStart (this, result);
		}

		public SingleSteppingEngine (ThreadManager manager, ProcessServant process,
					     Inferior inferior, int pid)
			: this (manager, process, inferior.ProcessStart)
		{
			this.inferior = inferior;
			this.pid = pid;
		}

		public CommandResult StartThread (bool do_attach)
		{
			CommandResult result = new ThreadCommandResult (thread);
			if (do_attach)
				current_operation = new OperationInitialize (this, result);
			else
				current_operation = new OperationRun (this, result);
			return result;
		}

		internal void InitAfterFork ()
		{
			CommandResult result = new ThreadCommandResult (thread);
			current_operation = new OperationRun (this, result);
			PushOperation (new OperationInitAfterFork (this));
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
			if (inferior == null)
				return;

			ProcessEvent (inferior.ProcessEvent (status));
		}

		public void ProcessEvent (Inferior.ChildEvent cevent)
		{
			Report.Debug (DebugFlags.EventLoop, "{0} received event {1} {2}",
				      this, cevent, stop_requested ? " - stop requested" : "");

			if (killed && (cevent.Type != Inferior.ChildEventType.CHILD_EXITED)) {
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} when already killed",
					      this, cevent);
				return;
			}

			if (has_thread_lock) {
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} at {3} while being thread-locked ({2})",
					      this, cevent, stop_event, inferior.CurrentFrame);
				if (stop_event != null)
					throw new InternalError ();
				stop_event = cevent;
				stopped = true;
				return;
			}
			if (cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION)
				Report.Debug (DebugFlags.Notification,
					      "{0} received event {1} {2}",
					      this, cevent, (NotificationType) cevent.Argument);
			else if ((cevent.Type != Inferior.ChildEventType.CHILD_EXITED) &&
				 (cevent.Type != Inferior.ChildEventType.CHILD_SIGNALED))
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} at {2} while running {3}",
					      this, cevent, inferior.CurrentFrame,
					      current_operation);
			else
				Report.Debug (DebugFlags.EventLoop,
					      "{0} received event {1} while running {2}",
					      this, cevent, current_operation);

			if ((cevent.Type == Inferior.ChildEventType.CHILD_EXITED) ||
			    (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED)) {
				Report.Debug (DebugFlags.SSE, "{0} is now dead!", this);
				dead = true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_INTERRUPTED) {
				stop_requested = false;
				frame_changed (inferior.CurrentFrame, null);
				OperationCompleted (new TargetEventArgs (TargetEventType.TargetInterrupted, 0, current_frame));
				return;
			}

			bool resume_target;
			if (manager.HandleChildEvent (this, inferior, ref cevent, out resume_target)) {
				Report.Debug (DebugFlags.EventLoop,
					      "{0} done handling event: {1} {2} {3} {4}",
					      this, cevent, resume_target, stop_requested,
					      has_thread_lock);
				if (!resume_target)
					return;
				if (stop_requested) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
					OperationCompleted (new TargetEventArgs (TargetEventType.TargetStopped, 0, current_frame));
					return;
				}
				inferior.Continue ();
				return;
			}
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

			TargetEventArgs result = null;

			if ((message == Inferior.ChildEventType.THROW_EXCEPTION) ||
			    (message == Inferior.ChildEventType.HANDLE_EXCEPTION)) {
				TargetAddress info = new TargetAddress (
					inferior.AddressDomain, cevent.Data1);
				TargetAddress ip = new TargetAddress (
					manager.AddressDomain, cevent.Data2);

				Report.Debug (DebugFlags.EventLoop,
					      "{0} received exception: {1} {2} {3}",
					      this, message, info, ip);

				TargetAddress stack = inferior.ReadAddress (info);
				TargetAddress exc = inferior.ReadAddress (info + inferior.TargetAddressSize);

				bool stop_on_exc;
				if (message == Inferior.ChildEventType.THROW_EXCEPTION)
					stop_on_exc = throw_exception (stack, exc, ip);
				else
					stop_on_exc = handle_exception (stack, exc, ip);

				Report.Debug (DebugFlags.SSE,
					      "{0} {1}stopping at exception ({2}:{3}:{4}) - {5} - {6}",
					      this, stop_on_exc ? "" : "not ", stack, exc, ip,
					      current_operation, temp_breakpoint_id);

				if (stop_on_exc) {
					inferior.WriteInteger (info + 2 * inferior.TargetAddressSize, 1);
					PushOperation (new OperationException (this, ip, exc, false));
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

					Breakpoint bpt = lookup_breakpoint (arg);
					Report.Debug (DebugFlags.SSE,
						      "{0} hit temporary breakpoint {1} at {2} {3}",
						      this, arg, inferior.CurrentFrame, bpt);
					if ((bpt == null) || !bpt.Breaks (thread.ID)) {
						message = Inferior.ChildEventType.CHILD_STOPPED;
						arg = 0;
						cevent = new Inferior.ChildEvent (
							Inferior.ChildEventType.CHILD_STOPPED, 0, 0, 0);
					} else {
						ProcessChildEvent (cevent, result);
						return;
					}
				}
			}

			if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
				// Ok, the next thing we need to check is whether this is actually "our"
				// breakpoint or whether it belongs to another thread.  In this case,
				// `step_over_breakpoint' does everything for us and we can just continue
				// execution.
				bool remain_stopped = child_breakpoint (cevent, arg);
				if (stop_requested) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
					if (remain_stopped)
						result = new TargetEventArgs (TargetEventType.TargetHitBreakpoint, arg, current_frame);
					else
						result = new TargetEventArgs (TargetEventType.TargetStopped, 0, current_frame);
				} else if (!remain_stopped) {
					do_continue ();
					return;
				}
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

				PushOperation (new OperationException (this, ip, exc, true));
				return;
			}

			case Inferior.ChildEventType.CHILD_HIT_BREAKPOINT:
				break;

			case Inferior.ChildEventType.CHILD_SIGNALED:
				if (killed)
					result = new TargetEventArgs (TargetEventType.TargetExited, 0);
				else
					result = new TargetEventArgs (TargetEventType.TargetSignaled, arg);
				break;

			case Inferior.ChildEventType.CHILD_EXITED:
				result = new TargetEventArgs (TargetEventType.TargetExited, arg);
				break;

			case Inferior.ChildEventType.CHILD_CALLBACK_COMPLETED:
				frame_changed (inferior.CurrentFrame, null);
				result = new TargetEventArgs (
					TargetEventType.TargetStopped, 0,
					current_frame);
				break;
			}

			ProcessChildEvent (cevent, result);
		}

		protected void ProcessChildEvent (Inferior.ChildEvent cevent, TargetEventArgs result)
		{
			Inferior.ChildEventType message = cevent.Type;
			int arg = (int) cevent.Argument;

		send_result:
			// If `result' is not null, then the target stopped abnormally.
			if (result != null) {
				// Ok, inform the user that we stopped.
				OperationCompleted (result);
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

				if (!current_operation.ProcessEvent (cevent, out result, out send_result))
					return;

				if (result != null)
					goto send_result;

				if (!send_result) {
					OperationCompleted (null);
					return;
				}
			}

			//
			// Ok, the target stopped normally.  Now we need to compute the
			// new stack frame and then send the result to our caller.
			//
			TargetAddress frame = inferior.CurrentFrame;

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
			if (pending_bpt >= 0) {
				Breakpoint bpt = lookup_breakpoint (pending_bpt);
				if ((bpt != null) && bpt.Breaks (thread.ID) && !bpt.HideFromUser) {
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

		void OperationCompleted (TargetEventArgs result)
		{
			lock (this) {
				remove_temporary_breakpoint ();
				engine_stopped = true;
				last_target_event = result;
				Report.Debug (DebugFlags.EventLoop, "{0} completed operation {1}: {2}",
					      this, current_operation, result);
				if (result != null)
					manager.Debugger.SendTargetEvent (this, result);
				if (current_operation != null) {
					Report.Debug (DebugFlags.EventLoop, "{0} setting completed: {1}",
						      this, current_operation.Result);
					current_operation.Result.Completed ();
					current_operation = null;
				}
			}
		}

		internal void OnManagedThreadCreated (TargetAddress end_stack_address)
		{
			this.end_stack_address = end_stack_address;
		}

		internal void SetTID (long tid)
		{
			this.tid = tid;
		}

		internal void SetManagedThreadData (TargetAddress lmf_address,
						    TargetAddress extended_notifications_addr)
		{
			this.lmf_address = lmf_address;
			this.extended_notifications_addr = extended_notifications_addr;
		}

		internal void SetMainReturnAddress (TargetAddress main_ret)
		{
			this.main_retaddr = main_ret + inferior.TargetAddressSize;
			this.reached_main = true;
		}

		internal void OnManagedThreadExited ()
		{
			this.end_stack_address = TargetAddress.Null;
			process.OnManagedThreadExitedEvent (this);
		}

		internal void OnThreadExited (Inferior.ChildEvent cevent)
		{
			TargetEventArgs result;
			int arg = (int) cevent.Argument;
			if (killed)
				result = new TargetEventArgs (TargetEventType.TargetExited, 0);
			else if (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED)
				result = new TargetEventArgs (TargetEventType.TargetSignaled, arg);
			else
				result = new TargetEventArgs (TargetEventType.TargetExited, arg);
			temp_breakpoint_id = 0;
			OperationCompleted (result);

			process.OnThreadExitedEvent (this);
			Dispose ();
		}

		Breakpoint lookup_breakpoint (int index)
		{
			BreakpointHandle handle = process.BreakpointManager.LookupBreakpoint (index);
			if (handle == null)
				return null;

			return handle.Breakpoint;
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
				if (!engine_stopped || (has_thread_lock && (pending_operation != null))) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					throw new TargetException (TargetError.NotStopped);
				}

				engine_stopped = false;
				last_target_event = null;
			}
		}

		object SendCommand (TargetAccessDelegate target)
		{
			if (inferior == null)
				throw new TargetException (TargetError.NoTarget);

			if (ThreadManager.InBackgroundThread)
				return target (thread, null);
			else
				return manager.SendCommand (this, target, null);
		}

		CommandResult StartOperation (Operation operation)
		{
			StartOperation ();

			return (CommandResult) SendCommand (delegate {
				return ProcessOperation (operation);
			});
		}

		CommandResult ProcessOperation (Operation operation)
		{
			stop_requested = false;

			if (has_thread_lock) {
				Report.Debug (DebugFlags.SSE,
					      "{0} starting {1} while being thread-locked",
					      this, operation);
				pending_operation = operation;
				return operation.Result;
			} else
				Report.Debug (DebugFlags.SSE,
					      "{0} starting {1}", this, operation);

			current_operation = operation;
			ExecuteOperation (operation);
			return operation.Result;
		}

		void PushOperation (Operation operation)
		{
			current_operation.PushOperation (operation);
			ExecuteOperation (operation);
		}

		void ExecuteOperation (Operation operation)
		{
			try {
				check_inferior ();
				operation.Execute ();
			} catch (Exception ex) {
				Report.Debug (DebugFlags.SSE, "{0} caught exception while " +
					      "processing operation {1}: {2}", this, operation, ex);
				operation.Result.Result = ex;
				OperationCompleted (null);
			}
		}

		public override TargetEventArgs LastTargetEvent {
			get { return last_target_event; }
		}

		public override Method Lookup (TargetAddress address)
		{
			process.UpdateSymbolTable (inferior);
			Method method = process.SymbolTableManager.Lookup (address);
			Report.Debug (DebugFlags.JitSymtab, "{0} lookup {1}: {2}",
				      this, address, method);
			return method;
		}

		public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			return process.SymbolTableManager.SimpleLookup (address, exact_match);
		}

#region public properties
		internal Inferior Inferior {
			get { return inferior; }
		}

		internal override Architecture Architecture {
			get { return inferior.Architecture; }
		}

		public Thread Thread {
			get { return thread; }
		}

		public override int PID {
			get { return pid; }
		}

		public override long TID {
			get { return tid; }
		}

		public override bool IsAlive {
			get { return !dead && !killed && (inferior != null); }
		}

		public override TargetAddress LMFAddress {
			get { return lmf_address; }
		}

		public override bool CanRun {
			get { return true; }
		}

		public override bool CanStep {
			get { return true; }
		}

		public override bool IsStopped {
			get { return engine_stopped; }
		}

		internal override ProcessServant ProcessServant {
			get { return process; }
		}

		internal override ThreadManager ThreadManager {
			get { return manager; }
		}

		public override Backtrace CurrentBacktrace {
			get { return current_backtrace; }
		}

		public override StackFrame CurrentFrame {
			get { return current_frame; }
		}

		public override Method CurrentMethod {
			get { return current_method; }
		}

		public override TargetAddress CurrentFrameAddress {
			get { return inferior.CurrentFrame; }
		}

		protected MonoDebuggerInfo MonoDebuggerInfo {
			get { return process.MonoManager.MonoDebuggerInfo; }
		}

		public override TargetState State {
			get {
				if (inferior == null)
					return TargetState.NoTarget;
				else
					return inferior.State;
			}
		}
#endregion

		protected TargetAddress EndStackAddress {
			get { return end_stack_address; }
		}

		public override TargetMemoryInfo TargetMemoryInfo {
			get {
				check_inferior ();
				return inferior.TargetMemoryInfo;
			}
		}

		public override TargetMemoryArea[] GetMemoryMaps ()
		{
			check_inferior ();
			return inferior.GetMemoryMaps ();
		}

		public override void Kill ()
		{
			killed = true;
			SendCommand (delegate {
				inferior.Kill ();
				return null;
			});
		}

		internal override object DoTargetAccess (TargetAccessHandler func)
		{
			return SendCommand (delegate {
				return func (inferior);
			});
		}

		public override void Detach ()
		{
			SendCommand (delegate {
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					throw new TargetException (TargetError.NotStopped);
				}

				process.AcquireGlobalThreadLock (this);
				process.BreakpointManager.RemoveAllBreakpoints (inferior);

				if (process.MonoManager != null)
					process.MonoManager.Detach (inferior);
				DoDetach ();
				return null;
			});
		}

		protected void DoDetach ()
		{
			foreach (ThreadServant servant in process.ThreadServants)
				servant.DetachThread ();
		}

		internal override void DetachThread ()
		{
			if (inferior != null) {
				inferior.Detach ();
				inferior.Dispose ();
				inferior = null;
			}

			OperationCompleted (new TargetEventArgs (TargetEventType.TargetExited, 0));
			process.OnThreadExitedEvent (this);
			Dispose ();
		}

		public override void Stop ()
		{
			lock (this) {
				Report.Debug (DebugFlags.EventLoop, "{0} interrupt: {1} {2}",
					      this, engine_stopped, current_operation);

				if (engine_stopped || stop_requested)
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
					process.ReleaseGlobalThreadLock (this);
				}

				if (!stopped) {
					// We're already stopped, so just consider the
					// current operation as finished.
					engine_stopped = true;
					stop_requested = false;

					frame_changed (inferior.CurrentFrame, null);
					TargetEventArgs args = new TargetEventArgs (
						TargetEventType.FrameChanged, current_frame);
					OperationCompleted (args);
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
		//   Such unknown breakpoints are handled by the Debugger; one of
		//   the language backends may recognize the breakpoint's address, for
		//   instance if this is the JIT's breakpoint trampoline.
		//
		//   Returns true if the target should remain stopped and false to
		//   continue stepping.
		//
		//   If we can't find a handler for the breakpoint, the default is to stop
		//   the target and let the user decide what to do.
		// </summary>
		bool child_breakpoint (Inferior.ChildEvent cevent, int index)
		{
			// The inferior knows about breakpoints from all threads, so if this is
			// zero, then no other thread has set this breakpoint.
			if (index == 0)
				return true;

			Breakpoint bpt = lookup_breakpoint (index);
			if ((bpt == null) || !bpt.Breaks (thread.ID))
				return false;

			if (!process.BreakpointManager.IsBreakpointEnabled (index))
				return false;

			bool remain_stopped;
			if (bpt.BreakpointHandler (inferior, out remain_stopped))
				return remain_stopped;

			TargetAddress address = inferior.CurrentFrame;
			return bpt.CheckBreakpointHit (thread, address);
		}

		bool step_over_breakpoint (bool singlestep, TargetAddress until)
		{
			int index;
			bool is_enabled;
			process.BreakpointManager.LookupBreakpoint (
				inferior.CurrentFrame, out index, out is_enabled);

			if ((index == 0) || !is_enabled)
				return false;

			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over breakpoint {1} at {2} until {3}",
				      this, index, inferior.CurrentFrame, until);

			Instruction instruction = inferior.Architecture.ReadInstruction (
				inferior, inferior.CurrentFrame);

			if ((instruction == null) || !instruction.HasInstructionSize ||
			    !process.CanExecuteCode) {
				PushOperation (new OperationStepOverBreakpoint (this, index, until));
				return true;
			}

			if (instruction.InterpretInstruction (inferior)) {
				if (!singlestep)
					return false;

				byte[] nop_insn = Architecture.Opcodes.GenerateNopInstruction ();
				PushOperation (new OperationExecuteInstruction (this, nop_insn, false));
				return true;
			}

			if (instruction.IsIpRelative) {
				PushOperation (new OperationStepOverBreakpoint (this, index, until));
				return true;
			}

			PushOperation (new OperationExecuteInstruction (this, instruction.Code, true));
			return true;
		}

		void enable_extended_notification (NotificationType type)
		{
			long notifications = inferior.ReadLongInteger (extended_notifications_addr);
			notifications |= (uint) type;
			inferior.WriteLongInteger (extended_notifications_addr, notifications);
		}

		void disable_extended_notification (NotificationType type)
		{
			long notifications = inferior.ReadLongInteger (extended_notifications_addr);
			notifications &= ~(long) type;
			inferior.WriteLongInteger (extended_notifications_addr, notifications);
		}

		bool throw_exception (TargetAddress stack, TargetAddress exc, TargetAddress ip)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} throwing exception {1} at {2} while running {3}", this, exc, ip,
				      current_operation);

			if ((current_operation != null) && (current_operation.StartFrame != null) &&
			    (current_operation.StartFrame.Address == ip))
				return false;

			if (current_operation is OperationRuntimeInvoke)
				return false;

			foreach (ExceptionCatchPoint handle in exception_handlers.Values) {
				Report.Debug (DebugFlags.SSE,
					      "{0} invoking exception handler {1} for {0}",
					      this, handle.Name, exc);

				if (!handle.CheckException (process.MonoLanguage, inferior, exc))
					continue;

				Report.Debug (DebugFlags.SSE,
					      "{0} stopped on exception {1} at {2}", this, exc, ip);

				return true;
			}

			return false;
		}

		bool handle_exception (TargetAddress stack, TargetAddress exc, TargetAddress ip)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} handling exception {1} at {2} while running {3}", this, exc, ip,
				      current_operation);

			if (current_operation == null)
				return true;

			return current_operation.HandleException (stack, exc);
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
			    Method.IsInSameMethod (current_method, address))
				same_method = true;
			else
				current_method = Lookup (address);

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();
			registers = inferior.GetRegisters ();

			if (!main_retaddr.IsNull && (iframe.StackPointer >= main_retaddr))
				return new OperationRun (this, false, operation.Result);

			// Compute the current stack frame.
			if ((current_method != null) && current_method.HasLineNumbers) {
				Block block = current_method.LookupBlock (inferior, address);
				if (block != null) {
					if (block.BlockType == Block.Type.IteratorDispatcher)
						return new OperationStepIterator (
							this, current_method, operation.Result);
				}

				SourceAddress source = current_method.LineNumberTable.Lookup (address);

				if (!same_method) {
					// If check_method_operation() returns true, it already
					// started a stepping operation, so the target is
					// currently running.
					Operation new_operation = check_method_operation (
						address, current_method, source, operation);
					if (new_operation != null)
						return new_operation;
				}

				if (source != null)
					update_current_frame (new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers, current_method, source));
				else
					update_current_frame (new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers, current_method));
			} else {
				if (!same_method && (current_method != null)) {
					Operation new_operation = check_method_operation (
						address, current_method, null, operation);
					if (new_operation != null)
						return new_operation;
				}

				if (current_method != null)
					update_current_frame (new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers, current_method));
				else {
					Symbol name;
					try {
						name = SimpleLookup (address, false);
					} catch {
						name = null;
					}
					update_current_frame (new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers, thread.NativeLanguage,
						name));
				}
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
		Operation check_method_operation (TargetAddress address, Method method,
						  SourceAddress source, Operation operation)
		{
			// Do nothing if this is not a source stepping operation.
			if ((operation == null) || !operation.IsSourceOperation)
				return null;

			if (method.WrapperType != WrapperType.None)
				return new OperationWrapper (this, method, operation.Result);

			if (method.IsIterator)
				return new OperationStepIterator (this, method, operation.Result);

			Language language = method.Module.Language;
			if (source == null)
				return null;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				// We stopped between two source lines.  This normally
				// happens when returning from a method call; in this
				// case, we need to continue stepping until we reach the
				// next source line.
				return new OperationStep (this, new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					null, language, StepMode.SourceLine), operation.Result);
			}

			LineNumberTable lnt = method.LineNumberTable;
			if (lnt.HasMethodBounds && (address < lnt.MethodStartAddress)) {
				return new OperationStep (this, new StepFrame (
					method.StartAddress, lnt.MethodStartAddress, null,
					null, StepMode.Finish), operation.Result);
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				// Do not stop inside a method's prologue code, but stop
				// immediately behind it (on the first instruction of the
				// method's actual code).
				return new OperationStep (this, new StepFrame (
					method.StartAddress, method.MethodStartAddress, null,
					null, StepMode.Finish), operation.Result);
			}

			return null;
		}

		void frames_invalid ()
		{
			current_frame = null;
			current_backtrace = null;
			registers = null;
		}

		void update_current_frame (StackFrame new_frame)
		{
			current_frame = new_frame;
		}

		int temp_breakpoint_id = 0;
		void insert_temporary_breakpoint (TargetAddress address)
		{
			check_inferior ();
			int dr_index;

			if (temp_breakpoint_id != 0)
				throw new InternalError ("FUCK");

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
			Instruction instruction = inferior.Architecture.ReadInstruction (
				inferior, address);
			if ((instruction == null) || !instruction.HasInstructionSize) {
				do_step_native ();
				return;
			}

			Report.Debug (DebugFlags.SSE, "{0} do_next_native: {1} {2}", this,
				      address, instruction.InstructionType);

			// Step one instruction unless this is a call
			if (!instruction.IsCall) {
				do_step_native ();
				return;
			}

			// Insert a temporary breakpoint immediately behind it and continue.
			address += instruction.InstructionSize;
			do_continue (address);
		}

		// <summary>
		//   Resume the target.
		// </summary>
		void do_continue ()
		{
			do_continue (TargetAddress.Null);
		}

		void do_continue (TargetAddress until)
		{
			check_inferior ();
			frames_invalid ();

			if (step_over_breakpoint (false, until))
				return;

			if (!until.IsNull)
				insert_temporary_breakpoint (until);
			inferior.Continue ();
		}

		void remove_temporary_breakpoint ()
		{
			Report.Debug (DebugFlags.SSE, "{0} remove temp breakpoint {1}",
				      this, temp_breakpoint_id);

			if (temp_breakpoint_id != 0) {
				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}
		}

		void do_step_native ()
		{
			if (step_over_breakpoint (true, TargetAddress.Null))
				return;

			inferior.Step ();
		}

		protected bool CheckTrampoline (Instruction instruction, TrampolineHandler handler)
		{
			TargetAddress trampoline;
			Instruction.TrampolineType type = instruction.CheckTrampoline (
				inferior, out trampoline);
			if (type == Instruction.TrampolineType.None)
				return false;

			Report.Debug (DebugFlags.SSE,
				      "{0} found trampoline {1}:{2} at {3} while running {4}",
				      this, type, trampoline, instruction.Address, current_operation);

			if (type == Instruction.TrampolineType.NativeTrampolineStart) {
				PushOperation (new OperationNativeTrampoline (this, trampoline, handler));
				return true;
			} else if (type == Instruction.TrampolineType.NativeTrampoline) {
				Method method = Lookup (trampoline);
				if (!MethodHasSource (method))
					do_next_native ();
				else
					do_continue (trampoline);
				return true;
			} else if (type == Instruction.TrampolineType.MonoTrampoline) {
				PushOperation (new OperationMonoTrampoline (
					this, instruction, trampoline, handler));
				return true;
			} else if (type == Instruction.TrampolineType.DelegateInvoke) {
				PushOperation (new OperationDelegateInvoke (this));
				return true;
			}

			return false;
		}

		protected bool MethodHasSource (Method method)
		{
			if ((method == null) || !method.HasLineNumbers || !method.HasMethodBounds)
				return false;

			if (method.WrapperType == WrapperType.ManagedToNative) {
				DebuggerConfiguration config = process.Session.Config;
				ModuleGroup native_group = config.GetModuleGroup ("native");
				if (!native_group.StepInto)
					return false;
			}

			if (current_method != null) {
				if ((method.Module != current_method.Module) && !method.Module.StepInto)
					return false;
			} else {
				if (!method.Module.StepInto)
					return false;
			}

			if (!method.HasSource || method.IsWrapper)
				return false;

			LineNumberTable lnt = method.LineNumberTable;
			if (lnt == null)
				return false;

			SourceAddress addr = lnt.Lookup (method.MethodStartAddress);
			if (addr == null) {
				Report.Error ("OOOOPS - No source for method: {0}", method);
				lnt.DumpLineNumbers ();
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
			Language language = (frame.Method != null) ? frame.Method.Module.Language : null;

			if (frame.SourceAddress == null)
				return new StepFrame (language, StepMode.SingleInstruction);

			// The current source line started at the current address minus
			// SourceOffset; the next source line will start at the current
			// address plus SourceRange.

			int offset = frame.SourceAddress.SourceOffset;
			int range = frame.SourceAddress.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, frame, language, StepMode.StepFrame);
		}

		// <summary>
		//   Create a step frame for a native stepping operation.
		// </summary>
		StepFrame CreateStepFrame (StepMode mode)
		{
			check_inferior ();
			Language language = (current_method != null) ?
				current_method.Module.Language : null;

			return new StepFrame (language, mode);
		}

		StackData save_stack (long id)
		{
			//
			// Save current state.
			//
			StackData stack_data = new StackData (
				id, current_method, inferior.CurrentFrame, current_frame,
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
			current_frame = stack.Frame;
			current_backtrace = stack.Backtrace;
			registers = stack.Registers;
		}

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		internal override void AcquireThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} acquiring thread lock: {1} {2}",
				      this, engine_stopped, current_operation);

			has_thread_lock = true;
			stopped = inferior.Stop (out stop_event);

			Report.Debug (DebugFlags.Threads,
				      "{0} acquiring thread lock #1: {1} {2}",
				      this, stopped, stop_event);

			if ((stop_event != null) &&
			    ((stop_event.Type == Inferior.ChildEventType.CHILD_EXITED) ||
			     ((stop_event.Type == Inferior.ChildEventType.CHILD_SIGNALED))))
				return;

			TargetAddress new_rsp = inferior.PushRegisters ();

			Report.Debug (DebugFlags.Threads,
				      "{0} acquired thread lock: {1} {2} {3} {4} {5}",
				      this, stopped, stop_event, EndStackAddress,
				      new_rsp, inferior.CurrentFrame);

			if (!EndStackAddress.IsNull)
				inferior.WriteAddress (EndStackAddress, new_rsp);
		}

		internal override void ReleaseThreadLock ()
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} releasing thread lock: {1} {2} {3} {4}",
				      this, stopped, stop_event, inferior.CurrentFrame,
				      current_operation);

			if (stop_event != null)
				manager.AddPendingEvent (this, stop_event);

			has_thread_lock = false;

			inferior.PopRegisters ();
		}

		internal void ReleaseThreadLock (Inferior.ChildEvent cevent)
		{
			Report.Debug (DebugFlags.Threads,
				      "{0} releasing thread lock #1: {1} {2} {3}",
				      this, cevent, inferior.CurrentFrame,
				      current_operation);

			// The target stopped before we were able to send the SIGSTOP,
			// but we haven't processed this event yet.
			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument == 0)) {
				if (current_operation != null)
					current_operation.ResumeOperation ();

				return;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_INTERRUPTED) {
				do_continue ();
				return;
			}

			ProcessEvent (cevent);
		}

		internal override void ReleaseThreadLockDone ()
		{
			if (pending_operation == null)
				return;

			Report.Debug (DebugFlags.Threads, "{0} starting pending operation {1}", this,
				      pending_operation);

			current_operation = pending_operation;
			pending_operation = null;
			ExecuteOperation (current_operation);
		}

		internal bool OnModuleLoaded ()
		{
			Inferior.StackFrame iframe = inferior.GetCurrentFrame ();
			Registers registers = inferior.GetRegisters ();

			StackFrame main_frame;
			if (process.MonoManager != null) {
				MonoLanguageBackend mono = process.MonoLanguage;

				MonoFunctionType main = mono.MainMethod;

				MethodSource source = main.SymbolFile.GetMethodByToken (main.Token);
				if (source != null) {
					SourceLocation location = new SourceLocation (source);

					main_frame = new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers, main, location);
				} else {
					main_frame = new StackFrame (
						thread, iframe.Address, iframe.StackPointer,
						iframe.FrameAddress, registers);
				}
				update_current_frame (main_frame);
			} else {
				Method method = Lookup (inferior.MainMethodAddress);

				main_frame = new StackFrame (
					thread, iframe.Address, iframe.StackPointer,
					iframe.FrameAddress, registers, method);
				update_current_frame (main_frame);
			}

			Queue pending = new Queue ();
			foreach (Event e in process.Session.Events) {
				Breakpoint breakpoint = e as Breakpoint;
				if (breakpoint == null)
					continue;

				if (!e.IsEnabled || e.IsActivated)
					continue;

				try {
					BreakpointHandle handle = breakpoint.Resolve (thread, main_frame);
					if (handle == null)
						continue;

					FunctionBreakpointHandle fh = handle as FunctionBreakpointHandle;
					if (fh == null) {
						handle.Insert (thread);
						continue;
					}

					pending.Enqueue (fh);
				} catch (TargetException ex) {
					Report.Error ("Cannot insert breakpoint {0}: {1}",
						      e.Index, ex.Message);
				} catch (Exception ex) {
					Report.Error ("Cannot insert breakpoint {0}: {1}",
						      e.Index, ex.Message);
				}
			}

			if (pending.Count == 0)
				return false;

			PushOperation (new OperationActivateBreakpoints (this, pending));
			return true;
		}

		public override string ToString ()
		{
			return String.Format ("SSE ({0}:{1}:{2:x})", ID, PID, TID);
		}

#region SSE Commands

		public override void StepInstruction (CommandResult result)
		{
			StartOperation (new OperationStep (this, StepMode.SingleInstruction, result));
		}

		public override void StepNativeInstruction (CommandResult result)
		{
			StartOperation (new OperationStep (this, StepMode.NativeInstruction, result));
		}

		public override void NextInstruction (CommandResult result)
		{
			StartOperation (new OperationStep (this, StepMode.NextInstruction, result));
		}

		public override void StepLine (CommandResult result)
		{
			StartOperation (new OperationStep (this, StepMode.SourceLine, result));
		}

		public override void NextLine (CommandResult result)
		{
			StartOperation (new OperationStep (this, StepMode.NextLine, result));
		}

		public override void Finish (bool native, CommandResult result)
		{
			StartOperation (new OperationFinish (this, native, result));
		}

		public override void Continue (TargetAddress until, CommandResult result)
		{
			StartOperation (new OperationRun (this, until, false, result));
		}

		public override void Background (TargetAddress until, CommandResult result)
		{
			StartOperation (new OperationRun (this, until, true, result));
		}

		public override void RuntimeInvoke (TargetFunctionType function,
						    TargetStructObject object_argument,
						    TargetObject[] param_objects,
						    bool is_virtual, bool debug,
						    RuntimeInvokeResult result)
		{
			StartOperation (new OperationRuntimeInvoke (
				this, function, object_argument, param_objects,
				is_virtual, debug, result));
		}

		public override CommandResult CallMethod (TargetAddress method, long arg1, long arg2,
							  long arg3, string string_argument)
		{
			return StartOperation (new OperationCallMethod (
				this, method, arg1, arg2, arg3, string_argument));
		}

		public override CommandResult CallMethod (TargetAddress method, long arg1, long arg2)
		{
			return StartOperation (new OperationCallMethod (this, method, arg1, arg2));
		}

		public override CommandResult Return (bool run_finally)
		{
			return (CommandResult) SendCommand (delegate {
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					throw new TargetException (TargetError.NotStopped);
				}

				if (current_frame == null)
					throw new TargetException (TargetError.NoStack);

				process.UpdateSymbolTable (inferior);

				Backtrace bt = new Backtrace (current_frame);
				bt.GetBacktrace (this, inferior, Backtrace.Mode.Native,
						 TargetAddress.Null, 2);

				if (bt.Count < 2)
					throw new TargetException (TargetError.NoStack);

				StackFrame parent_frame = bt.Frames [1];
				if (parent_frame == null)
					return null;

				if (!process.IsManagedApplication || !run_finally) {
					inferior.AbortInvoke (parent_frame.StackPointer);
					inferior.SetRegisters (parent_frame.Registers);
					frame_changed (inferior.CurrentFrame, null);
					TargetEventArgs args = new TargetEventArgs (
						TargetEventType.TargetStopped, 0, current_frame);
					manager.Debugger.SendTargetEvent (this, args);
					return null;
				}

				MonoLanguageBackend language = process.MonoLanguage;
				return StartOperation (new OperationReturn (this, language, parent_frame));
			});
		}

		public override CommandResult AbortInvocation ()
		{
			return (CommandResult) SendCommand (delegate {
				GetBacktrace (Backtrace.Mode.Native, -1);
				if (current_backtrace == null)
					throw new TargetException (TargetError.NoStack);

				if (!process.IsManagedApplication)
					throw new InvalidOperationException ();

				return StartOperation (new OperationAbortInvocation (
					this, current_backtrace));
			});
		}

		public override Backtrace GetBacktrace (Backtrace.Mode mode, int max_frames)
		{
			return (Backtrace) SendCommand (delegate {
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "{0} not stopped", this);
					throw new TargetException (TargetError.NotStopped);
				}

				process.UpdateSymbolTable (inferior);

				if (current_frame == null)
					throw new TargetException (TargetError.NoStack);

				current_backtrace = new Backtrace (current_frame);

				current_backtrace.GetBacktrace (
					this, inferior, mode, TargetAddress.Null, max_frames);

				return current_backtrace;
			});
		}

		public override Registers GetRegisters ()
		{
			return (Registers) SendCommand (delegate {
				registers = inferior.GetRegisters ();
				return registers;
			});
		}

		public override void SetRegisters (Registers registers)
		{
			if (!registers.FromCurrentFrame)
				throw new InvalidOperationException ();

			this.registers = registers;
			SendCommand (delegate {
				inferior.SetRegisters (registers);
				return registers;
			});
		}

		internal override void InsertBreakpoint (BreakpointHandle handle,
							 TargetAddress address, int domain)
		{
			SendCommand (delegate {
				process.BreakpointManager.InsertBreakpoint (
					inferior, handle, address, domain);
				return null;
			});
		}

		internal override void RemoveBreakpoint (BreakpointHandle handle)
		{
			SendCommand (delegate {
				process.BreakpointManager.RemoveBreakpoint (inferior, handle);
				return null;
			});
		}

		static int next_event_index = 0;
		public override int AddEventHandler (Event handle)
		{
			if (handle.Type != EventType.CatchException)
				throw new InternalError ();

			int index = ++next_event_index;
			exception_handlers.Add (index, handle);
			return index;
		}

		public override void RemoveEventHandler (int index)
		{
			exception_handlers.Remove (index);
		}

		public override int GetInstructionSize (TargetAddress address)
		{
			return (int) SendCommand (delegate {
				return Architecture.Disassembler.GetInstructionSize (inferior, address);
			});
		}

		public override AssemblerLine DisassembleInstruction (Method method, TargetAddress address)
		{
			return (AssemblerLine) SendCommand (delegate {
				return Architecture.Disassembler.DisassembleInstruction (
					inferior, method, address);
			});
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			return (AssemblerMethod) SendCommand (delegate {
				return Architecture.Disassembler.DisassembleMethod (inferior, method);
			});
		}

		public override byte[] ReadBuffer (TargetAddress address, int size)
		{
			return (byte[]) SendCommand (delegate {
				return inferior.ReadBuffer (address, size);
			});
		}

		public override TargetBlob ReadMemory (TargetAddress address, int size)
		{
			return new TargetBlob (ReadBuffer (address, size), TargetMemoryInfo);
		}

		public override byte ReadByte (TargetAddress address)
		{
			return (byte) SendCommand (delegate {
				return inferior.ReadByte (address);
			});
		}

		public override int ReadInteger (TargetAddress address)
		{
			return (int) SendCommand (delegate {
				return inferior.ReadInteger (address);
			});
		}

		public override long ReadLongInteger (TargetAddress address)
		{
			return (long) SendCommand (delegate {
				return inferior.ReadLongInteger (address);
			});
		}

		public override TargetAddress ReadAddress (TargetAddress address)
		{
			return (TargetAddress) SendCommand (delegate {
				return inferior.ReadAddress (address);
			});
		}

		public override string ReadString (TargetAddress address)
		{
			return (string) SendCommand (delegate {
				return inferior.ReadString (address);
			});
		}

		internal override Registers GetCallbackFrame (TargetAddress stack_pointer,
							      bool exact_match)
		{
			return (Registers) SendCommand (delegate {
				return inferior.GetCallbackFrame (stack_pointer, exact_match);
			});
		}

		public override void WriteBuffer (TargetAddress address, byte[] buffer)
		{
			SendCommand (delegate {
				inferior.WriteBuffer (address, buffer);
				return null;
			});
		}

		public override void WriteByte (TargetAddress address, byte value)
		{
			SendCommand (delegate {
				inferior.WriteByte (address, value);
				return null;
			});
		}

		public override void WriteInteger (TargetAddress address, int value)
		{
			SendCommand (delegate {
				inferior.WriteInteger (address, value);
				return null;
			});
		}

		public override void WriteLongInteger (TargetAddress address, long value)
		{
			SendCommand (delegate {
				inferior.WriteLongInteger (address, value);
				return null;
			});
		}

		public override void WriteAddress (TargetAddress address, TargetAddress value)
		{
			SendCommand (delegate {
				inferior.WriteAddress (address, value);
				return null;
			});
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override string PrintObject (Style style, TargetObject obj,
						    DisplayFormat format)
		{
			return (string) SendCommand (delegate {
				return style.FormatObject (thread, obj, format);
			});
		}

		public override string PrintType (Style style, TargetType type)
		{
			return (string) SendCommand (delegate {
				return style.FormatType (thread, type);
			});
		}

		internal override object Invoke (TargetAccessDelegate func, object data)
		{
			return SendCommand (delegate {
				return func (thread, data);
			});
		}
#endregion

#region IDisposable implementation
		protected override void DoDispose ()
		{
			if (inferior != null) {
				inferior.Dispose ();
				inferior = null;
			}

			base.DoDispose ();
		}
#endregion

		protected Method current_method;
		protected StackFrame current_frame;
		protected Backtrace current_backtrace;
		protected Registers registers;

		bool stopped;
		Inferior.ChildEvent stop_event;
		Operation current_operation;
		Operation pending_operation;

		Inferior inferior;
		Disassembler disassembler;
		Hashtable exception_handlers;
		bool engine_stopped;
		bool stop_requested;
		bool has_thread_lock;
		bool is_main, reached_main;
		bool killed, dead;
		long tid;
		int pid;

		int stepping_over_breakpoint;

		TargetEventArgs last_target_event;

		TargetAddress lmf_address = TargetAddress.Null;
		TargetAddress end_stack_address = TargetAddress.Null;
		TargetAddress extended_notifications_addr = TargetAddress.Null;
		TargetAddress main_retaddr = TargetAddress.Null;

#region Nested SSE classes
		protected sealed class StackData : DebuggerMarshalByRefObject
		{
			public readonly long ID;
			public readonly Method Method;
			public readonly TargetAddress Address;
			public readonly StackFrame Frame;
			public readonly Backtrace Backtrace;
			public readonly Registers Registers;

			public StackData (long id, Method method, TargetAddress address,
					  StackFrame frame, Backtrace backtrace,
					  Registers registers)
			{
				this.ID = id;
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
			AskParent,
			ResumeOperation
		}

		public abstract bool IsSourceOperation {
			get;
		}

		protected bool HasChild {
			get { return child != null; }
		}

		protected readonly SingleSteppingEngine sse;
		protected readonly Inferior inferior;

		public readonly CommandResult Result;
		public Inferior.StackFrame StartFrame;
		public int PendingBreakpoint = -1;

		protected Operation (SingleSteppingEngine sse, CommandResult result)
		{
			this.sse = sse;
			this.inferior = sse.inferior;

			if (result != null)
				this.Result = result;
			else
				this.Result = new SimpleCommandResult (this);
		}

		public virtual void Execute ()
		{
			StartFrame = inferior.GetCurrentFrame (true);
			Report.Debug (DebugFlags.SSE, "{0} executing {1} at {2}",
				      sse, this, StartFrame != null ?
				      StartFrame.Address : TargetAddress.Null);
			DoExecute ();
		}

		protected abstract void DoExecute ();

		protected virtual void Abort ()
		{
			sse.Stop ();
		}

		public virtual bool ResumeOperation ()
		{
			return false;
		}

		Operation child;

		public void PushOperation (Operation op)
		{
			if (child != null)
				child.PushOperation (op);
			else
				child = op;
		}

		public virtual bool ProcessEvent (Inferior.ChildEvent cevent,
						  out TargetEventArgs args, out bool send_result)
		{
			EventResult result = ProcessEvent (cevent, out args);

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

		protected virtual EventResult ProcessEvent (Inferior.ChildEvent cevent,
							    out TargetEventArgs args)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_INTERRUPTED) {
				args = null;
				if (ResumeOperation ())
					return EventResult.Running;
			}

			if (child != null) {
				EventResult result = child.ProcessEvent (cevent, out args);

				if ((result != EventResult.AskParent) &&
				    (result != EventResult.ResumeOperation))
					return result;

				Operation old_child = child;
				child = null;

				if ((result == EventResult.ResumeOperation) && ResumeOperation ()) {
					args = null;
					return EventResult.Running;
				}

				Report.Debug (DebugFlags.EventLoop,
					      "{0} resending event {1} from {2} to {3}",
					      sse, cevent, old_child, this);
			}

			return DoProcessEvent (cevent, out args);
		}

		protected abstract EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args);

		public virtual bool HandleException (TargetAddress stack, TargetAddress exc)
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
				return String.Format ("{0} ({1})", GetType ().Name, MyToString ());
			else
				return String.Format ("{0}:{1}", GetType ().Name, child);
		}

		protected class SimpleCommandResult : CommandResult
		{
			Operation operation;
			ManualResetEvent completed_event = new ManualResetEvent (false);

			internal SimpleCommandResult (Operation operation)
			{
				this.operation = operation;
			}

			public override WaitHandle CompletedEvent {
				get { return completed_event; }
			}

			public override void Abort ()
			{
				operation.Abort ();
			}

			public override void Completed ()
			{
				completed_event.Set ();
			}
		}
	}

	protected class OperationStart : Operation
	{
		public OperationStart (SingleSteppingEngine sse, CommandResult result)
			: base (sse, result)
		{ }

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{ }

		bool initialized;

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} start: {1} {2} {3} {4}", sse, initialized,
				      cevent, sse.ProcessServant.IsAttached,
				      inferior.CurrentFrame);

			args = null;
			if ((cevent.Type != Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK))
				return EventResult.Completed;

			if (sse.ProcessServant.IsAttached) {
				if (sse.ProcessServant.IsManaged)
					sse.ProcessServant.MonoManager.InitializeAfterAttach (inferior);
				return EventResult.Completed;
			}

			if (sse.Architecture.IsSyscallInstruction (inferior, inferior.CurrentFrame)) {
				inferior.Step ();
				return EventResult.Running;
			}

			if (!sse.ProcessServant.IsManaged) {
				if (sse.OnModuleLoaded ())
					return EventResult.Running;
			}

			Report.Debug (DebugFlags.SSE,
				      "{0} start #1: {1} {2} {3}", sse, cevent,
				      sse.ProcessServant.IsAttached, inferior.MainMethodAddress);
			sse.PushOperation (new OperationRun (sse, true, Result));
			return EventResult.Running;
		}

		public override bool HandleException (TargetAddress stack, TargetAddress exc)
		{
			return sse.reached_main ? false : true;
		}
	}

	protected class OperationActivateBreakpoints : Operation
	{
		public OperationActivateBreakpoints (SingleSteppingEngine sse, Queue pending)
			: base (sse, null)
		{
			this.pending_events = pending;
		}

		protected override void DoExecute ()
		{
			do_execute ();
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		Queue pending_events;
		bool completed;

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			args = null;

			Report.Debug (DebugFlags.SSE,
				      "{0} activate breakpoints: {1}", sse, completed);

			while (!completed) {
				if (do_execute ())
					return EventResult.Running;

				Report.Debug (DebugFlags.SSE,
					      "{0} activate breakpoints done - continue", sse);

				return EventResult.ResumeOperation;
			}

			Report.Debug (DebugFlags.SSE,
				      "{0} activate breakpoints completed", sse);
			return EventResult.AskParent;
		}

		bool do_execute ()
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} activate breakpoints execute: {1} {2}", sse,
				      inferior.CurrentFrame, pending_events.Count);

			if (pending_events.Count == 0) {
				completed = true;
				return false;
			}

			FunctionBreakpointHandle handle =
				(FunctionBreakpointHandle) pending_events.Dequeue ();

			Report.Debug (DebugFlags.SSE,
				      "{0} activate breakpoints: {1}", sse, handle);

			sse.PushOperation (new OperationInsertBreakpoint (sse, handle));
			return true;
		}
	}

	protected class OperationInsertBreakpoint : OperationCallback
	{
		public readonly FunctionBreakpointHandle Handle;

		public OperationInsertBreakpoint (SingleSteppingEngine sse,
						  FunctionBreakpointHandle handle)
			: base (sse, null)
		{
			this.Handle = handle;
		}

		protected override void DoExecute ()
		{
			MonoDebuggerInfo info = sse.ProcessServant.MonoManager.MonoDebuggerInfo;
			MonoLanguageBackend mono = sse.process.MonoLanguage;

			MonoFunctionType func = (MonoFunctionType) Handle.Function;
			TargetAddress image = func.SymbolFile.MonoImage;
			int index = MonoLanguageBackend.GetUniqueID ();

			mono.RegisterMethodLoadHandler (index, Handle.MethodLoaded);

			inferior.CallMethod (
				info.InsertSourceBreakpoint, image.Address,
				func.Token, index, func.DeclaringType.BaseName, ID);
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} insert breakpoint done: {1:x} {2:x}",
				      sse, data1, data2);

			return EventResult.AskParent;
		}
	}

	protected class OperationInitialize : Operation
	{
		public OperationInitialize (SingleSteppingEngine sse, CommandResult result)
			: base (sse, result)
		{ }

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{ }

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} initialize ({1})", sse,
				      DebuggerWaitHandle.CurrentThread);

			args = null;
			return EventResult.Completed;
		}
	}

	protected class OperationInitAfterFork : Operation
	{
		public OperationInitAfterFork (SingleSteppingEngine sse)
			: base (sse, null)
		{ }

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute ()
		{ }

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} init after fork ({1})", sse,
				      DebuggerWaitHandle.CurrentThread);

			sse.ProcessServant.BreakpointManager.InitializeAfterFork (inferior);

			args = null;
			return EventResult.AskParent;
		}
	}

	protected class OperationInitCodeBuffer : OperationCallback
	{
		public OperationInitCodeBuffer (SingleSteppingEngine sse)
			: base (sse, null)
		{ }

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute ()
		{
			MonoDebuggerInfo info = sse.ProcessServant.MonoManager.MonoDebuggerInfo;
			inferior.CallMethod (info.InitCodeBuffer, 0, 0, ID);
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} init code buffer: {1:x} {2:x} {3}",
				      sse, data1, data2, Result);

			TargetAddress buffer = new TargetAddress (inferior.AddressDomain, data1);
			sse.process.MonoManager.InitCodeBuffer (inferior, buffer);

			RestoreStack ();
			return EventResult.AskParent;
		}
	}

	protected class OperationStepOverBreakpoint : Operation
	{
		TargetAddress until;
		public readonly int Index;
		bool has_thread_lock;

		public OperationStepOverBreakpoint (SingleSteppingEngine sse, int index,
						    TargetAddress until)
			: base (sse, null)
		{
			this.Index = index;
			this.until = until;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute ()
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over breakpoint: {1}", sse, until);

			sse.process.AcquireGlobalThreadLock (sse);
			inferior.DisableBreakpoint (Index);

			has_thread_lock = true;

			Report.Debug (DebugFlags.SSE,
				      "{0} stepping over breakpoint {1} at {2} until {3} ({4})",
				      sse, Index, inferior.CurrentFrame, until, sse.current_method);

			inferior.Step ();
		}

		bool ReleaseThreadLock (Inferior.ChildEvent cevent)
		{
			if (!has_thread_lock)
				return true;

			Report.Debug (DebugFlags.SSE,
				      "{0} releasing thread lock at {1}",
				      sse, inferior.CurrentFrame);

			inferior.EnableBreakpoint (Index);
			sse.process.ReleaseGlobalThreadLock (sse);

			Report.Debug (DebugFlags.SSE,
				      "{0} done releasing thread lock at {1} - {2}",
				      sse, inferior.CurrentFrame, sse.has_thread_lock);

			has_thread_lock = false;

			if (!sse.has_thread_lock)
				return true;

			sse.stopped = true;
			sse.stop_event = cevent;
			return false;
		}

		protected override EventResult ProcessEvent (Inferior.ChildEvent cevent,
							     out TargetEventArgs args)
		{
			if (((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			     (cevent.Argument == 0)) ||
			    (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK)) {
				if (!ReleaseThreadLock (cevent)) {
					args = null;
					return EventResult.Running;
				}
			}
			return base.ProcessEvent (cevent, out args);
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
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
			return EventResult.ResumeOperation;
		}
	}

	protected class OperationExecuteInstruction : Operation
	{
		public readonly byte[] Instruction;
		public readonly bool UpdateIP;

		bool pushed_code_buffer;

		public OperationExecuteInstruction (SingleSteppingEngine sse, byte[] insn,
						    bool update_ip)
			: base (sse, null)
		{
			this.Instruction = insn;
			this.UpdateIP = update_ip;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute ()
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} executing instruction: {1}", sse,
				      TargetBinaryReader.HexDump (Instruction));

			if (!sse.ProcessServant.MonoManager.HasCodeBuffer) {
				sse.PushOperation (new OperationInitCodeBuffer (sse));
				pushed_code_buffer = true;
				return;
			}

			inferior.ExecuteInstruction (Instruction, UpdateIP);
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} executed instruction {1} at {2}: {3}",
				      sse, Instruction, inferior.CurrentFrame, cevent);

			args = null;
			if (pushed_code_buffer) {
				pushed_code_buffer = false;
				inferior.ExecuteInstruction (Instruction, UpdateIP);
				return EventResult.Running;
			}

			return EventResult.ResumeOperation;
		}
	}

	protected abstract class OperationStepBase : Operation
	{
		protected OperationStepBase (SingleSteppingEngine sse, CommandResult result)
			: base (sse, result)
		{ }

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			args = null;
			bool completed;
			if (cevent.Type == Inferior.ChildEventType.CHILD_INTERRUPTED)
				completed = !ResumeOperation ();
			else
				completed = DoProcessEvent ();

			return completed ? EventResult.Completed : EventResult.Running;
		}

		protected abstract bool DoProcessEvent ();

		protected abstract bool TrampolineHandler (Method method);
	}

	protected class OperationStep : OperationStepBase
	{
		public StepMode StepMode;
		public StepFrame StepFrame;

		public OperationStep (SingleSteppingEngine sse, StepMode mode, CommandResult result)
			: base (sse, result)
		{
			this.StepMode = mode;
		}

		public OperationStep (SingleSteppingEngine sse, StepFrame frame, CommandResult result)
			: base (sse, result)
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

		protected override void DoExecute ()
		{
			switch (StepMode) {
			case StepMode.NativeInstruction:
				sse.do_step_native ();
				break;

			case StepMode.NextInstruction:
				sse.do_next_native ();
				break;

			case StepMode.SourceLine:
				if (StepFrame == null)
					StepFrame = sse.CreateStepFrame ();
				if (StepFrame == null)
					sse.do_step_native ();
				else
					Step (true);
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
					Step (true);
				}
				break;

			case StepMode.SingleInstruction:
				StepFrame = sse.CreateStepFrame (StepMode.SingleInstruction);
				Step (true);
				break;

			case StepMode.Finish:
				Step (true);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		public override bool ResumeOperation ()
		{
			Report.Debug (DebugFlags.SSE, "{0} resuming operation {1}", sse, this);

			if (sse.temp_breakpoint_id != 0) {
				inferior.Continue ();
				return true;
			}

			return !Step (false);
		}

		public override bool HandleException (TargetAddress stack, TargetAddress exc)
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

			StackFrame oframe = StepFrame.StackFrame;

			Report.Debug (DebugFlags.SSE,
				      "{0} handling exception: {1} {2} - {3} {4} - {5}", sse,
				      StepFrame, oframe, stack, oframe.StackPointer,
				      stack < oframe.StackPointer);

			if (stack < oframe.StackPointer)
				return false;

			return true;
		}

		protected override bool TrampolineHandler (Method method)
		{
			if (StepMode == StepMode.SingleInstruction)
				return true;

			if (method == null)
				return false;

			if (method.WrapperType == WrapperType.DelegateInvoke)
				return true;
			else if (method.WrapperType == WrapperType.Alloc)
				return false;

			if (StepMode == StepMode.SourceLine)
				return sse.MethodHasSource (method);

			return true;
		}

		protected bool Step (bool first)
		{
			if (StepFrame == null)
				return true;

			TargetAddress current_frame = inferior.CurrentFrame;
			bool in_frame = sse.is_in_step_frame (StepFrame, current_frame);
			Report.Debug (DebugFlags.SSE, "{0} stepping at {1} in {2} ({3}in frame)",
				      sse, current_frame, StepFrame, !in_frame ? "not " : "");

			if (!first && !in_frame)
				return true;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the specified step frame.
			 */
			Instruction instruction = inferior.Architecture.ReadInstruction (
				inferior, current_frame);
			if ((instruction == null) || !instruction.IsCall) {
				sse.do_step_native ();
				return false;
			}

			if (!instruction.HasInstructionSize) {
				/* Ooops, we don't know anything about this instruction */
				sse.do_step_native ();
				return false;
			}

			TargetAddress call_target = instruction.GetEffectiveAddress (inferior);

			if ((sse.current_method != null) && (sse.current_method.HasMethodBounds) &&
			    !call_target.IsNull &&
			    (call_target >= sse.current_method.MethodStartAddress) &&
			    (call_target < sse.current_method.MethodEndAddress)) {
				/* Intra-method call (we stay outside the prologue/epilogue code,
				 * so this also can't be a recursive call). */
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

			if (sse.CheckTrampoline (instruction, TrampolineHandler))
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
			Method method = sse.Lookup (call_target);

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

			if (!sse.MethodHasSource (method)) {
				sse.do_next_native ();
				return false;
			}

			/*
			 * Finally, step into the method.
			 */
			sse.do_step_native ();
			return false;
		}

		protected override bool DoProcessEvent ()
		{
			Report.Debug (DebugFlags.SSE, "{0} processing {1} event.",
				      sse, this);
			return Step (false);
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

		public OperationRun (SingleSteppingEngine sse, TargetAddress until,
				     bool in_background, CommandResult result)
			: base (sse, result)
		{
			this.until = until;
			this.in_background = in_background;
		}

		public OperationRun (SingleSteppingEngine sse, bool in_background,
				     CommandResult result)
			: this (sse, TargetAddress.Null, in_background, result)
		{ }

		public OperationRun (SingleSteppingEngine sse, CommandResult result)
			: this (sse, TargetAddress.Null, true, result)
		{ }


		public bool InBackground {
			get { return in_background; }
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			if (!until.IsNull)
				sse.do_continue (until);
			else
				sse.do_continue ();
		}

		public override bool ResumeOperation ()
		{
			Report.Debug (DebugFlags.SSE, "{0} resuming operation {1}", sse, this);

			sse.do_continue ();
			return true;
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			args = null;
			if (!until.IsNull && inferior.CurrentFrame == until)
				return EventResult.Completed;
			Report.Debug (DebugFlags.EventLoop, "{0} received {1} at {2} in {3}",
				      sse, cevent, inferior.CurrentFrame, this);
			if ((cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) ||
			    (cevent.Type == Inferior.ChildEventType.CHILD_CALLBACK))
				return EventResult.Completed;
			Execute ();
			return EventResult.Running;
		}

		public override bool HandleException (TargetAddress stack, TargetAddress exc)
		{
			return false;
		}
	}

	protected class OperationFinish : OperationStepBase
	{
		public readonly bool Native;

		public OperationFinish (SingleSteppingEngine sse, bool native, CommandResult result)
			: base (sse, result)
		{
			this.Native = native;
		}

		public override bool IsSourceOperation {
			get { return !Native; }
		}

		StepFrame step_frame;
		TargetAddress until;

		protected override void DoExecute ()
		{
			if (!Native) {
				StackFrame frame = sse.CurrentFrame;
				if (frame.Method == null)
					throw new TargetException (TargetError.NoMethod);

				step_frame = new StepFrame (
					frame.Method.StartAddress, frame.Method.EndAddress,
					frame, null, StepMode.Finish);
			} else {
				Inferior.StackFrame frame = inferior.GetCurrentFrame ();
				until = frame.StackPointer;

				Report.Debug (DebugFlags.SSE,
					      "{0} starting finish native until {1} {2}",
					      sse, until, sse.temp_breakpoint_id);
			}

			sse.do_next_native ();
		}

		public override bool ResumeOperation ()
		{
			Report.Debug (DebugFlags.SSE, "{0} resuming operation {1}", sse, this);

			if (sse.temp_breakpoint_id != 0) {
				inferior.Continue ();
				return true;
			}

			return !DoProcessEvent ();
		}

		protected override bool DoProcessEvent ()
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

		protected override bool TrampolineHandler (Method method)
		{
			return false;
		}
	}

	protected abstract class OperationCallback : Operation
	{
		public readonly long ID = ++next_id;
		StackData stack_data;

		static int next_id = 0;

		protected OperationCallback (SingleSteppingEngine sse)
			: base (sse, null)
		{ }

		protected OperationCallback (SingleSteppingEngine sse, CommandResult result)
			: base (sse, result)
		{ }

		public override void Execute ()
		{
			stack_data = sse.save_stack (ID);
			try {
				base.Execute ();
			} catch {
				RestoreStack ();
				throw;
			}
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.EventLoop,
				      "{0} received event {1} at {2} while waiting for " +
				      "callback {4}:{3}", sse, cevent, inferior.CurrentFrame,
				      ID, this);

			args = null;
			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument == 0)) {
				sse.do_continue ();
				return EventResult.Running;
			} else if (cevent.Type != Inferior.ChildEventType.CHILD_CALLBACK) {
				Report.Debug (DebugFlags.SSE,
					      "{0} aborting callback {1} ({2}) at {3}: {4}",
					      sse, this, ID, inferior.CurrentFrame, cevent);
				AbortOperation ();
				return EventResult.Completed;
			}

			if (ID != cevent.Argument) {
				Report.Debug (DebugFlags.SSE,
					      "{0} aborting callback {1} ({2}) at {3}: {4}",
					      sse, this, ID, inferior.CurrentFrame, cevent);
				AbortOperation ();
				return EventResult.Completed;
			}

			try {
				args = null;
				return CallbackCompleted (cevent);
			} catch {
				RestoreStack ();
				return EventResult.CompletedCallback;
			}
		}

		protected virtual EventResult CallbackCompleted (Inferior.ChildEvent cevent)
		{
			return CallbackCompleted (cevent.Data1, cevent.Data2);
		}

		protected abstract EventResult CallbackCompleted (long data1, long data2);

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected void AbortOperation ()
		{
			stack_data = null;
		}

		protected void RestoreStack ()
		{
			if (stack_data != null)
				sse.restore_stack (stack_data);
			stack_data = null;
		}

		protected void DiscardStack ()
		{
			stack_data = null;
		}
	}

	protected class OperationRuntimeInvoke : OperationCallback
	{
		new public readonly RuntimeInvokeResult Result;
		public readonly MonoFunctionType Function;
		public readonly TargetObject[] ParamObjects;
		public readonly bool IsVirtual;
		public readonly bool Debug;

		private readonly TargetMemoryAccess internal_target;

		MonoLanguageBackend language;
		TargetAddress method = TargetAddress.Null;
		TargetAddress invoke = TargetAddress.Null;
		TargetStructObject instance;
		MonoClassInfo class_info;
		Stage stage;

		protected enum Stage {
			Uninitialized,
			ResolvedClass,
			BoxingInstance,
			HasMethodAddress,
			GettingVirtualMethod,
			HasVirtualMethod,
			CompilingMethod,
			CompiledMethod,
			InvokedMethod
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		public OperationRuntimeInvoke (SingleSteppingEngine sse,
					       TargetFunctionType function,
					       TargetStructObject instance,
					       TargetObject[] param_objects,
					       bool is_virtual, bool debug,
					       RuntimeInvokeResult result)
			: base (sse, result)
		{
			this.Result = result;
			this.Function = (MonoFunctionType) function;
			this.instance = instance;
			this.ParamObjects = param_objects;
			this.IsVirtual = is_virtual;
			this.Debug = debug;
			this.method = TargetAddress.Null;
			this.stage = Stage.Uninitialized;

			this.internal_target = inferior;
		}

		protected override void DoExecute ()
		{
			language = sse.process.MonoLanguage;

			class_info = Function.ResolveClass (inferior, false);
			if (class_info == null) {
				MonoClassType klass = Function.DeclaringType as MonoClassType;
				if (klass == null)
					throw new TargetException (TargetError.ClassNotInitialized,
								   "Class `{0}' not initialized yet.",
								   Function.DeclaringType.Name);

				TargetAddress image = Function.SymbolFile.MonoImage;
				int token = klass.Token;

				Report.Debug (DebugFlags.SSE,
					      "{0} rti resolving class {1}:{2:x}", sse, image, token);

				inferior.CallMethod (
					sse.MonoDebuggerInfo.LookupClass, image.Address, 0, 0,
					Function.DeclaringType.Name, ID);
				return;
			}

			stage = Stage.ResolvedClass;
			do_execute ();
		}

		void do_execute ()
		{
			switch (stage) {
			case Stage.ResolvedClass:
				if (!get_method_address ())
					return;
				goto case Stage.HasMethodAddress;

			case Stage.HasMethodAddress:
				if (!get_virtual_method ())
					return;
				goto case Stage.HasVirtualMethod;

			case Stage.HasVirtualMethod: {
				Report.Debug (DebugFlags.SSE,
					      "{0} rti compiling method: {1}", sse, method);

				stage = Stage.CompilingMethod;
				inferior.CallMethod (
					sse.MonoDebuggerInfo.CompileMethod, method.Address, 0, ID);
				return;
			}

			case Stage.CompiledMethod: {
				sse.insert_temporary_breakpoint (invoke);

				inferior.RuntimeInvoke (
					sse.MonoDebuggerInfo.RuntimeInvoke,
					method, instance, ParamObjects, ID, Debug);

				stage = Stage.InvokedMethod;
				return;
			}

			default:
				throw new InternalError ();
			}
		}

		bool get_method_address ()
		{
			method = class_info.GetMethodAddress (inferior, Function.Token);

			if ((instance == null) || instance.Type.IsByRef)
				return true;

			TargetType decl = Function.DeclaringType;
			if ((decl.Name != "System.ValueType") && (decl.Name != "System.Object"))
				return true;

			TargetStructType parent_type = instance.Type.GetParentType (inferior);

			if (!instance.Type.IsByRef && parent_type.IsByRef) {
				TargetAddress klass = ((MonoClassObject) instance).GetKlassAddress (inferior);
				stage = Stage.BoxingInstance;
				inferior.CallMethod (
					sse.MonoDebuggerInfo.GetBoxedObjectMethod, klass.Address,
					instance.Location.GetAddress (internal_target).Address, ID);
				return false;
			}

			return true;
		}

		bool get_virtual_method ()
		{
			if (!IsVirtual || (instance == null) || !instance.HasAddress ||
			    !instance.Type.IsByRef)
				return true;

			stage = Stage.GettingVirtualMethod;
			inferior.CallMethod (
				sse.MonoDebuggerInfo.GetVirtualMethod,
				instance.Location.GetAddress (internal_target).Address,
				method.Address, ID);
			return false;
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			switch (stage) {
			case Stage.Uninitialized: {
				TargetAddress klass = new TargetAddress (inferior.AddressDomain, data1);

				Report.Debug (DebugFlags.SSE,
					      "{0} rti resolved class: {1}", sse, klass);

				class_info = language.ReadClassInfo (inferior, klass);
				((IMonoStructType) Function.DeclaringType).ClassInfo = class_info;
				((IMonoStructType) Function.DeclaringType).ResolveClass (inferior, false);
				stage = Stage.ResolvedClass;
				do_execute ();
				return EventResult.Running;
			}

			case Stage.BoxingInstance: {
				TargetAddress boxed = new TargetAddress (inferior.AddressDomain, data1);

				Report.Debug (DebugFlags.SSE,
					      "{0} rti boxed object: {1}", sse, boxed);

				TargetLocation new_loc = new AbsoluteTargetLocation (boxed);
				TargetStructType parent_type = instance.Type.GetParentType (inferior);
				instance = (TargetStructObject) parent_type.GetObject (inferior, new_loc);
				stage = Stage.HasMethodAddress;
				do_execute ();
				return EventResult.Running;
			}

			case Stage.GettingVirtualMethod: {
				method = new TargetAddress (inferior.AddressDomain, data1);

				Report.Debug (DebugFlags.SSE,
					      "{0} rti got virtual method: {1}", sse, method);

				TargetAddress klass = inferior.ReadAddress (method + 8);
				TargetType class_type = language.ReadMonoClass (inferior, klass);

				if (class_type == null) {
					Result.ExceptionMessage = String.Format (
						"Unable to get virtual method `{0}'.", Function.FullName);
					Result.InvocationCompleted = true;
					RestoreStack ();
					return EventResult.CompletedCallback;
				}

				if (!class_type.IsByRef) {
					TargetLocation new_loc = instance.Location.GetLocationAtOffset (
						2 * inferior.TargetMemoryInfo.TargetAddressSize);
					instance = (TargetClassObject) class_type.GetObject (
						inferior, new_loc);
				}

				stage = Stage.HasVirtualMethod;
				do_execute ();
				return EventResult.Running;
			}

			case Stage.CompilingMethod: {
				invoke = new TargetAddress (inferior.AddressDomain, data1);

				Report.Debug (DebugFlags.SSE,
					      "{0} rti compiled method: {1}", sse, invoke);

				stage = Stage.CompiledMethod;
				do_execute ();
				return EventResult.Running;
			}

			case Stage.InvokedMethod: {
				Report.Debug (DebugFlags.SSE,
					      "{0} rti done: {1:x} {2:x}",
					      sse, data1, data2);

				if (data2 != 0) {
					TargetAddress exc_address = new TargetAddress (
						inferior.AddressDomain, data2);
					TargetFundamentalObject exc_obj = (TargetFundamentalObject)
						language.CreateObject (inferior, exc_address);

					Result.ExceptionMessage = (string) exc_obj.GetObject (inferior);
				}

				if (data1 != 0) {
					TargetAddress retval_address = new TargetAddress (
						inferior.AddressDomain, data1);

					Result.ReturnObject = language.CreateObject (
						inferior, retval_address);
				}

				Result.InvocationCompleted = true;
				RestoreStack ();
				return EventResult.CompletedCallback;
			}

			default:
				throw new InternalError ();
			}
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if (cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
				Report.Debug (DebugFlags.SSE,
					      "{0} hit breakpoint {1} at {2} during runtime-invoke",
					      sse, cevent.Argument, inferior.CurrentFrame);
			} else if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
				   (cevent.Argument == 0)) {
				Report.Debug (DebugFlags.SSE,
					      "{0} stopped at {1} during runtime-invoke",
					      sse, inferior.CurrentFrame);
			}

			if ((cevent.Type == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) ||
			    (cevent.Type == Inferior.ChildEventType.CHILD_STOPPED)) {
				if (inferior.CurrentFrame == invoke) {
					Report.Debug (DebugFlags.SSE,
						      "{0} stopped at invoke method {1}",
						      sse, invoke);

					inferior.MarkRuntimeInvokeFrame ();
				}

				args = null;
				if (Debug)
					return EventResult.Completed;
				else {
					sse.do_continue ();
					return EventResult.Running;
				}
			}

			return base.DoProcessEvent (cevent, out args);
		}

		public override bool HandleException (TargetAddress stack, TargetAddress exc)
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
		public readonly long Argument3;
		public readonly string StringArgument;

		public OperationCallMethod (SingleSteppingEngine sse,
					    TargetAddress method, long arg1, long arg2, long arg3,
					    string sarg)
			: base (sse)
		{
			this.Type = CallMethodType.LongLongLongString;
			this.Method = method;
			this.Argument1 = arg1;
			this.Argument2 = arg2;
			this.Argument3 = arg3;
			this.StringArgument = sarg;
		}

		public OperationCallMethod (SingleSteppingEngine sse,
					    TargetAddress method, long arg1, long arg2)
			: base (sse)
		{
			this.Type = CallMethodType.LongLong;
			this.Method = method;
			this.Argument1 = arg1;
			this.Argument2 = arg2;
		}

		bool interrupted_syscall;

		protected override void DoExecute ()
		{
			if (!interrupted_syscall &&
			    inferior.Architecture.IsSyscallInstruction (inferior, inferior.CurrentFrame)) {
				if (!sse.Process.CanExecuteCode)
					throw new TargetException (TargetError.InvocationException,
								   "Current thread stopped on a system " +
								   "call; cannot invoke any methods");

				/*
				 * The backend automatically sets %orig_rax to -1 before modifying %rip
				 * to prevent the kernel from restarting the system call.
				 *
				 * Unfortunately, the kernel clobbers %rcx, which may be used to pass
				 * parameters to the method.  Because of this, we need to execute a
				 * dummy instruction first.
				 */
				byte[] nop_insn = inferior.Architecture.Opcodes.GenerateNopInstruction ();
				sse.PushOperation (new OperationExecuteInstruction (sse, nop_insn, false));
				interrupted_syscall = true;
				return;
			}

			interrupted_syscall = false;

			switch (Type) {
			case CallMethodType.LongLong:
				inferior.CallMethod (Method, Argument1, Argument2, ID);
				break;

			case CallMethodType.LongLongLongString:
				inferior.CallMethod (Method, Argument1, Argument2, Argument3,
						     StringArgument, ID);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if (!interrupted_syscall)
				return base.DoProcessEvent (cevent, out args);

			Report.Debug (DebugFlags.EventLoop,
				      "{0} received event {1} at {2} while waiting for " +
				      "callback {4}:{3}", sse, cevent, inferior.CurrentFrame,
				      ID, this);

			args = null;
			if ((cevent.Type != Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument != 0)) {
				Report.Debug (DebugFlags.SSE,
					      "{0} aborting callback {1} ({2}) at {3}: {4}",
					      sse, this, ID, inferior.CurrentFrame, cevent);
				AbortOperation ();
				return EventResult.Completed;
			}

			DoExecute ();
			return EventResult.Running;
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			if (inferior.TargetAddressSize == 4)
				data1 &= 0xffffffffL;

			Report.Debug (DebugFlags.SSE,
				      "{0} call method done: {1:x} {2:x} {3}",
				      sse, data1, data2, Result);

			RestoreStack ();
			Result.Result = new TargetAddress (inferior.AddressDomain, data1);
			return EventResult.CompletedCallback;
		}
	}

	protected class OperationMonoTrampoline : Operation
	{
		public readonly Instruction CallSite;
		public readonly TargetAddress Trampoline;
		public readonly TrampolineHandler TrampolineHandler;

		bool compiled;

		public OperationMonoTrampoline (SingleSteppingEngine sse, Instruction call_site,
						TargetAddress trampoline, TrampolineHandler handler)
			: base (sse, null)
		{
			this.CallSite = call_site;
			this.Trampoline = trampoline;
			this.TrampolineHandler = handler;
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			sse.enable_extended_notification (NotificationType.Trampoline);
			sse.do_continue ();
		}

		protected void TrampolineCompiled (TargetAddress mono_method, TargetAddress code)
		{
			sse.disable_extended_notification (NotificationType.Trampoline);

			if (TrampolineHandler != null) {
				Method method = sse.Lookup (code);
				if (!TrampolineHandler (method)) {
					sse.do_continue (CallSite.Address + CallSite.InstructionSize);
					return;
				}
			}

			sse.do_continue (code);
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			if ((cevent.Type == Inferior.ChildEventType.CHILD_NOTIFICATION) &&
			    ((NotificationType) cevent.Argument == NotificationType.Trampoline)) {
				TargetAddress method = new TargetAddress (
					inferior.AddressDomain, cevent.Data1);
				TargetAddress code = new TargetAddress (
					inferior.AddressDomain, cevent.Data2);

				args = null;
				compiled = true;
				TrampolineCompiled (method, code);
				return EventResult.Running;
			}

			args = null;
			if (!compiled) {
				sse.disable_extended_notification (NotificationType.Trampoline);
				return EventResult.Completed;
			} else
				return EventResult.ResumeOperation;
		}
	}

	protected class OperationNativeTrampoline : Operation
	{
		public readonly TrampolineHandler TrampolineHandler;
		public readonly TargetAddress Trampoline;

		TargetAddress stack_pointer;
		bool entered_trampoline;
		bool done;

		public OperationNativeTrampoline (SingleSteppingEngine sse, TargetAddress trampoline,
						  TrampolineHandler handler)
			: base (sse, null)
		{
			this.TrampolineHandler = handler;
			this.Trampoline = trampoline;
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			Inferior.StackFrame frame = inferior.GetCurrentFrame ();
			stack_pointer = frame.StackPointer;

			Report.Debug (DebugFlags.SSE,
				      "{0} starting native trampoline {1} at {2}: {3}",
				      sse, Trampoline, frame.Address, stack_pointer);

			sse.do_continue (Trampoline);
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} native trampoline event: {1}", sse, cevent);

			args = null;

			Inferior.StackFrame frame = inferior.GetCurrentFrame ();

			if (done)
				return EventResult.Completed;

			if (!entered_trampoline) {
				stack_pointer = frame.StackPointer;

				sse.do_step_native ();
				entered_trampoline = true;
				return EventResult.Running;
			}

			if (frame.StackPointer <= stack_pointer) {
				sse.do_next_native ();
				return EventResult.Running;
			}

			done = true;

			Instruction instruction = sse.Architecture.ReadInstruction (
				inferior, frame.Address);
			if ((instruction == null) || !instruction.HasInstructionSize) {
				sse.do_step_native ();
				return EventResult.Running;
			}

			if (instruction.InstructionType != Instruction.Type.Jump) {
				sse.do_step_native ();
				return EventResult.Running;
			}

			return EventResult.Completed;
		}
	}

	protected class OperationException : Operation
	{
		TargetAddress ip;
		TargetAddress exc;
		bool unhandled;

		public OperationException (SingleSteppingEngine sse,
					   TargetAddress ip, TargetAddress exc, bool unhandled)
			: base (sse, null)
		{
			this.ip = ip;
			this.exc = exc;
			this.unhandled = unhandled;
		}

		public override bool IsSourceOperation {
			get { return false; }
		}

		protected override void DoExecute ()
		{
			sse.remove_temporary_breakpoint ();
			sse.do_continue (ip);
		}

		protected override EventResult DoProcessEvent (Inferior.ChildEvent cevent,
							       out TargetEventArgs args)
		{
			Report.Debug (DebugFlags.SSE,
				      "{0} processing OperationException at {1}: {2} {3} {4}",
				      sse, inferior.CurrentFrame, ip, exc, unhandled);
				      
			if (unhandled) {
				sse.frame_changed (inferior.CurrentFrame, null);
				sse.current_frame.SetExceptionObject (exc);
				args = new TargetEventArgs (
					TargetEventType.UnhandledException,
					exc, sse.current_frame);
				return EventResult.Completed;
			} else {
				sse.frame_changed (inferior.CurrentFrame, null);
				sse.current_frame.SetExceptionObject (exc);
				args = new TargetEventArgs (
					TargetEventType.Exception,
					exc, sse.current_frame);
				return EventResult.Completed;
			}
		}
	}

	protected class OperationWrapper : OperationStepBase
	{
		Method method;

		public OperationWrapper (SingleSteppingEngine sse,
					 Method method, CommandResult result)
			: base (sse, result)
		{
			this.method = method;
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			sse.do_step_native ();
		}

		protected override bool DoProcessEvent ()
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
			Instruction instruction = inferior.Architecture.ReadInstruction (
				inferior, current_frame);
			if ((instruction == null) || !instruction.HasInstructionSize) {
				sse.do_step_native ();
				return false;
			}

			if (sse.CheckTrampoline (instruction, TrampolineHandler))
				return false;

			sse.do_step_native ();
			return false;
		}

		protected override bool TrampolineHandler (Method method)
		{
			if (method == null)
				return false;

			if (method.WrapperType == WrapperType.DelegateInvoke)
				return true;

			return sse.MethodHasSource (method);
		}
	}

	protected class OperationDelegateInvoke : OperationStepBase
	{
		public OperationDelegateInvoke (SingleSteppingEngine sse)
			: base (sse, null)
		{ }

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			sse.do_step_native ();
		}

		bool finished;

		protected override bool DoProcessEvent ()
		{
			TargetAddress current_frame = inferior.CurrentFrame;

			Report.Debug (DebugFlags.SSE, "{0} delegate impl stopped at {1}",
				      sse, current_frame);

			if (finished)
				return true;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the current method.
			 */
			Instruction instruction = inferior.Architecture.ReadInstruction (
				inferior, current_frame);
			if ((instruction == null) || !instruction.HasInstructionSize) {
				sse.do_step_native ();
				return false;
			}

			Report.Debug (DebugFlags.SSE, "{0} delegate impl stopped at {1}: {2}",
				      sse, current_frame, instruction);

			if ((instruction.InstructionType == Instruction.Type.IndirectJump) ||
			    (instruction.InstructionType == Instruction.Type.IndirectCall))
				finished = true;

			sse.do_step_native ();
			return false;
		}

		protected override bool TrampolineHandler (Method method)
		{
			return false;
		}
	}

	protected class OperationStepIterator : OperationStepBase
	{
		Method method;

		public OperationStepIterator (SingleSteppingEngine sse,
					      Method method, CommandResult result)
			: base (sse, result)
		{
			this.method = method;
		}

		public override bool IsSourceOperation {
			get { return true; }
		}

		protected override void DoExecute ()
		{
			sse.do_next_native ();
		}

		protected override bool DoProcessEvent ()
		{
			TargetAddress current_frame = inferior.CurrentFrame;

			Report.Debug (DebugFlags.SSE, "{0} iterator stopped at {1} ({2}:{3})",
				      sse, current_frame, method.StartAddress, method.EndAddress);
			if ((current_frame < method.StartAddress) || (current_frame > method.EndAddress))
				return true;

			Block block = method.LookupBlock (inferior, current_frame);
			Report.Debug (DebugFlags.SSE, "{0} iterator block: {1}", sse, block);
			if ((block != null) && block.IsIteratorBody)
				return true;

			sse.do_next_native ();
			return false;
		}

		protected override bool TrampolineHandler (Method method)
		{
			if (method == null)
				return false;

			if (method.WrapperType == WrapperType.DelegateInvoke)
				return true;

			return sse.MethodHasSource (method);
		}
	}

	protected class OperationReturn : OperationCallback
	{
		public readonly MonoLanguageBackend Language;
		public readonly StackFrame ParentFrame;

		public OperationReturn (SingleSteppingEngine sse,
					MonoLanguageBackend language, StackFrame parent_frame)
			: base (sse)
		{
			this.Language = language;
			this.ParentFrame = parent_frame;
		}

		protected override void DoExecute ()
		{
			inferior.CallMethod (sse.MonoDebuggerInfo.RunFinally, null, ID);
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			DiscardStack ();
			inferior.AbortInvoke (ParentFrame.StackPointer);
			inferior.SetRegisters (ParentFrame.Registers);
			return EventResult.Completed;
		}
	}

	protected class OperationAbortInvocation : OperationCallback
	{
		public readonly Backtrace Backtrace;
		int level = 0;

		public OperationAbortInvocation (SingleSteppingEngine sse, Backtrace backtrace)
			: base (sse)
		{
			this.Backtrace = backtrace;
		}

		protected override void DoExecute ()
		{
			inferior.CallMethod (sse.MonoDebuggerInfo.RunFinally, null, ID);
		}

		protected override EventResult CallbackCompleted (long data1, long data2)
		{
			StackFrame parent_frame = Backtrace.Frames [++level];

			if (inferior.AbortInvoke (parent_frame.StackPointer)) {
				DiscardStack ();
				return EventResult.Completed;
			}

			inferior.SetRegisters (parent_frame.Registers);
			inferior.CallMethod (sse.MonoDebuggerInfo.RunFinally, null, ID);
			return EventResult.Running;
		}
	}
#endregion
	}

	[Serializable]
	internal enum CommandType {
		TargetAccess,
		CreateProcess
	}

	[Serializable]
	internal class Command {
		public SingleSteppingEngine Engine;
		public readonly CommandType Type;
		public object Data1, Data2;
		public object Result;

		public Command (SingleSteppingEngine sse, TargetAccessDelegate func, object data)
		{
			this.Type = CommandType.TargetAccess;
			this.Engine = sse;
			this.Data1 = func;
			this.Data2 = data;
		}

		public Command (CommandType type, object data)
		{
			this.Type = type;
			this.Data1 = data;
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
		LongLongLongString
	}
}
