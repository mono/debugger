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
public class SingleSteppingEngine : ThreadManager
{
	internal SingleSteppingEngine (DebuggerBackend backend)
		: base (backend)
	{
		this.SymbolTableManager = DebuggerBackend.SymbolTableManager;

		thread_hash = Hashtable.Synchronized (new Hashtable ());

		global_group = ThreadGroup.CreateThreadGroup ("global");
		thread_lock_mutex = new Mutex ();
		address_domain = new AddressDomain ("global");

		engine_event = new AutoResetEvent (false);
		start_event = new ManualResetEvent (false);
		completed_event = new ManualResetEvent (false);
		command_mutex = new Mutex ();

		wait_event = new AutoResetEvent (false);
		ready_event = new ManualResetEvent (false);
	}

	EngineProcess the_engine;
	protected readonly SymbolTableManager SymbolTableManager;

	ProcessStart start;
	Thread wait_thread;
	AutoResetEvent wait_event;
	Thread inferior_thread;
	ManualResetEvent ready_event;
	Hashtable thread_hash;

	Mutex thread_lock_mutex;
	AddressDomain address_domain;
	ThreadGroup global_group;

	ManualResetEvent start_event;
	ManualResetEvent completed_event;
	AutoResetEvent engine_event;
	Mutex command_mutex;
	bool result_sent = false;
	bool abort_requested;

	void start_wait_thread ()
	{
		while (!abort_requested)
			wait_thread_main ();
	}

	[DllImport("monodebuggerserver")]
	static extern int mono_debugger_server_wait (out long status);

	void wait_thread_main ()
	{
		Report.Debug (DebugFlags.Wait, "Wait thread waiting");

		while (!wait_event.WaitOne ())
			;

		Report.Debug (DebugFlags.Wait, "Wait thread woke up");

		if (abort_requested) {
			Report.Debug (DebugFlags.Wait, "Abort requested");
			return;
		}

		int pid;
		long status;

		pid = mono_debugger_server_wait (out status);
		if (pid < 0)
			throw new InternalError ();

		Report.Debug (DebugFlags.Wait,
			      "Wait thread received event: {0} {1:x}", pid, status);

		EngineProcess event_engine = (EngineProcess) thread_hash [pid];
		if (event_engine == null)
			throw new InternalError ("Got event {0:x} for unknown pid {1}",
						 status, pid);

		lock (this) {
			if (current_event_engine != null)
				throw new Exception ();
			current_event = status;
			current_event_engine = event_engine;
			engine_event.Set ();
		}
	}

	void start_inferior ()
	{
		the_engine = new EngineProcess (this, start);

		Report.Debug (DebugFlags.Threads, "Engine started: {0}", the_engine.PID);

		thread_hash.Add (the_engine.PID, the_engine);
		wait_event.Set ();

		OnThreadCreatedEvent (the_engine);

		while (!abort_requested) {
			engine_thread_main ();
		}
	}

	bool engine_is_ready = false;
	Exception start_error = null;

	// <remarks>
	//   These three variables are shared between the two threads, so you need to
	//   lock (this) before accessing/modifying them.
	// </remarks>
	Command current_command = null;
	CommandResult command_result = null;
	long current_event = 0;
	EngineProcess current_event_engine = null;

	void engine_error (Exception ex)
	{
		lock (this) {
			start_error = ex;
			start_event.Set ();
		}
	}

	// <remarks>
	//   This is only called on startup and blocks until the background thread
	//   has actually been started and it's waiting for commands.
	// </summary>
	void wait_until_engine_is_ready ()
	{
		while (!start_event.WaitOne ())
			;

		if (start_error != null)
			throw start_error;
	}

	public override Process StartApplication (ProcessStart start)
	{
		this.start = start;

		wait_thread = new Thread (new ThreadStart (start_wait_thread));
		wait_thread.Start ();

		inferior_thread = new Thread (new ThreadStart (start_inferior));
		inferior_thread.Start ();

		ready_event.WaitOne ();
		Wait ();

		OnInitializedEvent (main_process);
		OnMainThreadCreatedEvent (main_process);
		return main_process;
	}

	bool initialized;
	MonoThreadManager mono_manager;
	TargetAddress main_method = TargetAddress.Null;

	protected override void DoInitialize (Inferior inferior)
	{
		TargetAddress frame = inferior.CurrentFrame;
		if (frame != main_method)
			throw new InternalError ("Target stopped unexpectedly at {0}, " +
						 "but main is at {1}", frame, main_method);

		backend.ReachedMain ();

		ready_event.Set ();
	}

	public override Process[] Threads {
		get {
			lock (this) {
				Process[] procs = new Process [thread_hash.Count];
				int i = 0;
				foreach (EngineProcess engine in thread_hash.Values)
					procs [i] = engine;
				return procs;
			}
		}
	}

	// <summary>
	//   Stop all currently running threads without sending any notifications.
	//   The threads are automatically resumed to their previos state when
	//   ReleaseGlobalThreadLock() is called.
	// </summary>
	internal override void AcquireGlobalThreadLock (Inferior inferior, Process caller)
	{
	}

	internal override void ReleaseGlobalThreadLock (Inferior inferior, Process caller)
	{
	}

	internal override Inferior CreateInferior (ProcessStart start)
	{
		throw new NotSupportedException ();
	}

	protected override void AddThread (Inferior inferior, Process new_thread,
					   int pid, bool is_daemon)
	{
		Report.Debug (DebugFlags.Threads, "Add thread: {0} {1}",
			      new_thread, pid);
	}

	void thread_created (Inferior inferior, int pid)
	{
		Report.Debug (DebugFlags.Threads, "Thread created: {0}", pid);

		Inferior new_inferior = inferior.CreateThread ();

		EngineProcess new_thread = new EngineProcess (this, new_inferior, pid);

		thread_hash.Add (pid, new_thread);

		if ((mono_manager != null) &&
		    mono_manager.ThreadCreated (new_thread, new_inferior)) {
			main_process = new_thread;

			main_method = mono_manager.Initialize (the_engine, inferior);

			new_thread.Start (main_method, true);
		}

		new_inferior.Continue ();
		OnThreadCreatedEvent (new_thread);

		inferior.Continue ();
	}

	internal override bool HandleChildEvent (Inferior inferior,
						 Inferior.ChildEvent cevent)
	{
		if (!initialized) {
			if ((cevent.Type != Inferior.ChildEventType.CHILD_STOPPED) ||
			    (cevent.Argument != 0))
				throw new InternalError (
					"Received unexpected initial child event {0}", cevent);

			mono_manager = MonoThreadManager.Initialize (this, inferior);

			main_process = the_engine;
			if (mono_manager == null)
				main_method = inferior.MainMethodAddress;
			else
				main_method = TargetAddress.Null;
			the_engine.Start (main_method, true);

			initialized = true;
			return true;
		}

		if (cevent.Type == Inferior.ChildEventType.CHILD_CREATED_THREAD) {
			thread_created (inferior, (int) cevent.Argument);

			return true;
		}

		return false;
	}

	// <summary>
	//   Send the result of a command from the background thread back to the caller.
	// </summary>
	protected void SendResult (CommandResult result)
	{
		lock (this) {
			Report.Debug (DebugFlags.EventLoop, "Sending result {0}", result);
			command_result = result;
			completed_event.Set ();
		}
	}

	protected void SetCompleted ()
	{
		Report.Debug (DebugFlags.EventLoop, "Setting completed flag");
		result_sent = true;
		completed_event.Set ();
	}

	protected void ResetCompleted ()
	{
		Report.Debug (DebugFlags.EventLoop, "Clearing completed flag");
		completed_event.Reset ();
	}

	// <summary>
	//   Sends an asynchronous command to the background thread.  This is used
	//   for all stepping commands, no matter whether the user requested a
	//   synchronous or asynchronous operation since we can just block on the
	//   `completed_event' in the synchronous case.
	// </summary>
	protected void SendAsyncCommand (Command command, bool wait)
	{
		lock (this) {
			command.Process.SendTargetEvent (new TargetEventArgs (TargetEventType.TargetRunning, 0));
			current_command = command;
			engine_event.Set ();
			completed_event.Reset ();
		}

		WaitForCompletion (wait);
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
	protected bool CheckCanRun ()
	{
		if (!command_mutex.WaitOne (0, false)) {
			Console.WriteLine ("CANNOT GET COMMAND MUTEX");
			throw new InternalError ();
			return false;
		}
		if (!completed_event.WaitOne (0, false)) {
			Console.WriteLine ("CANNOT GET COMPLETED EVENT");
			command_mutex.ReleaseMutex ();
			return false;
		}
		return true;
	}

	protected void ReleaseCommandMutex ()
	{
		command_mutex.ReleaseMutex ();
	}

	// <summary>
	//   Sends a synchronous command to the background thread.  This is only
	//   used for non-steping commands such as getting a backtrace.
	// </summary>
	protected CommandResult SendSyncCommand (CommandFunc func, object data)
	{
		if (Thread.CurrentThread == inferior_thread) {
			try {
				return func (data);
			} catch (ThreadAbortException) {
				;
			} catch (Exception e) {
				return new CommandResult (e);
			}
		}

		if (!CheckCanRun ())
			return new CommandResult (CommandResultType.UnknownError);

		lock (this) {
			current_command = new Command (func, data);
			engine_event.Set ();
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

	protected void WaitForCompletion (bool wait)
	{
		if (wait)
			completed_event.WaitOne ();
		else
			completed_event.Set ();

		command_mutex.ReleaseMutex ();
	}

	internal void Wait ()
	{
		command_mutex.WaitOne ();

		completed_event.WaitOne ();

		command_mutex.ReleaseMutex ();
	}

	// <summary>
	//   The heart of the SingleSteppingEngine.  This runs in a background
	//   thread and processes stepping commands and events.
	// </summary>
	void engine_thread_main ()
	{
		Report.Debug (DebugFlags.Wait, "SSE waiting");

		// Wait until we get a command.
		while (!engine_event.WaitOne ())
			;

		Report.Debug (DebugFlags.Wait, "SSE woke up");

		if (abort_requested) {
			Report.Debug (DebugFlags.Wait, "Abort requested");
			return;
		}

		EngineProcess event_engine;
		long status;
		lock (this) {
			status = current_event;
			event_engine = current_event_engine;
			current_event = 0;
			current_event_engine = null;
		}

		if (event_engine != null) {
			try {
				event_engine.ProcessEvent (status);
			} catch (ThreadAbortException) {
				;
			} catch (Exception e) {
				Console.WriteLine ("EXCEPTION: {0}", e);
			}
			wait_event.Set ();

			if (!engine_is_ready) {
				engine_is_ready = true;
				start_event.Set ();
			}
		}

		Command command;
		lock (this) {
			command = current_command;
			current_command = null;
		}

		if (command == null)
			return;

		Report.Debug (DebugFlags.EventLoop, "SSE received command: {0}", command);

		// These are synchronous commands; ie. the caller blocks on us
		// until we finished the command and sent the result.
		if (command.Type == CommandType.Command) {
			CommandResult result;
			try {
				result = command.CommandFunc (command.CommandFuncData);
			} catch (ThreadAbortException) {
				;
				return;
			} catch (Exception e) {
				result = new CommandResult (e);
			}

			lock (this) {
				command_result = result;
				completed_event.Set ();
			}
		} else {
			try {
				command.Process.ProcessCommand (command);
			} catch (ThreadAbortException) {
				;
				return;
			} catch (Exception e) {
				Console.WriteLine ("EXCEPTION: {0} {1}", command, e);
			}
		}
	}

	public override AddressDomain AddressDomain {
		get { return address_domain; }
	}

	protected enum StepOperation {
		None,
		Initialize,
		Native,
		Run,
		RunInBackground,
		StepInstruction,
		StepNativeInstruction,
		NextInstruction,
		StepLine,
		NextLine,
		StepFrame,
		RuntimeInvoke
	}

	protected delegate CommandResult CommandFunc (object data);

	protected enum CommandType {
		StepOperation,
		Command
	}

	protected class Command {
		public TheEngine Process;
		public CommandType Type;
		public StepOperation Operation;
		public StepFrame StepFrame;
		public TargetAddress Until;
		public CommandFunc CommandFunc;
		public object CommandFuncData;

		public Command (TheEngine process, StepOperation operation, StepFrame frame)
		{
			this.Process = process;
			this.Type = CommandType.StepOperation;
			this.Operation = operation;
			this.StepFrame = frame;
			this.Until = TargetAddress.Null;
		}

		public Command (TheEngine process, StepOperation operation, TargetAddress until)
		{
			this.Process = process;
			this.Type = CommandType.StepOperation;
			this.Operation = operation;
			this.StepFrame = null;
			this.Until = until;
		}

		public Command (TheEngine process, StepOperation operation)
		{
			this.Process = process;
			this.Type = CommandType.StepOperation;
			this.Operation = operation;
			this.StepFrame = null;
			this.Until = TargetAddress.Null;
		}

		public Command (TheEngine process, StepOperation operation, object data)
		{
			this.Process = process;
			this.Type = CommandType.StepOperation;
			this.Operation = operation;
			this.StepFrame = null;
			this.Until = TargetAddress.Null;
			this.CommandFuncData = data;
		}

		public Command (CommandFunc func, object data)
		{
			this.Type = CommandType.Command;
			this.CommandFunc = func;
			this.CommandFuncData = data;
		}

		public override string ToString ()
		{
			return String.Format ("Command ({0}:{1}:{2}:{3}:{4}:{5}:{6})",
					      Process, Type, Operation, StepFrame, Until,
					      CommandFunc, CommandFuncData);
		}
	}

	protected enum CommandResultType {
		ChildEvent,
		CommandOk,
		UnknownError,
		Exception
	}

	protected class CommandResult {
		public readonly static CommandResult Ok = new CommandResult (CommandResultType.CommandOk);

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

		public CommandResult (CommandResultType type)
			: this (type, null)
		{ }

		public CommandResult (CommandResultType type, object data)
		{
			this.Type = type;
			this.Data = data;
		}

		public CommandResult (Exception e)
			: this (CommandResultType.Exception, e)
		{ }
	}

	protected abstract class TheEngine : NativeProcess
	{
		protected TheEngine (SingleSteppingEngine sse, Inferior inferior)
			: base (inferior.ProcessStart)
		{
			this.sse = sse;
			this.inferior = inferior;

			pid = inferior.PID;

			inferior.TargetOutput += new TargetOutputHandler (OnInferiorOutput);
			inferior.DebuggerOutput += new DebuggerOutputHandler (OnDebuggerOutput);
			inferior.DebuggerError += new DebuggerErrorHandler (OnDebuggerError);
		}

		public TheEngine (SingleSteppingEngine sse, ProcessStart start)
			: this (sse, Inferior.CreateInferior (sse.DebuggerBackend, start))
		{
			inferior.Run (true);
			pid = inferior.PID;

			is_main = true;

			setup_engine ();
		}

		public TheEngine (SingleSteppingEngine sse, Inferior inferior, int pid)
			: this (sse, inferior)
		{
			this.pid = pid;
			inferior.Attach (pid);

			is_main = false;
			tid = inferior.TID;

			setup_engine ();
		}

		void setup_engine ()
		{
			stop_event = new AutoResetEvent (false);
			restart_event = new AutoResetEvent (false);

			inferior.SingleSteppingEngine = sse;
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			Report.Debug (DebugFlags.Threads, "New SSE: {0}", this);

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			if (false) {
				sse.DebuggerBackend.ReachedMain ();
				main_method_retaddr = inferior.GetReturnAddress ();
			}

			disassembler.SymbolTable = sse.SymbolTableManager.SimpleSymbolTable;
			current_simple_symtab = sse.SymbolTableManager.SimpleSymbolTable;
			current_symtab = sse.SymbolTableManager.SymbolTable;

			native_language = new Mono.Debugger.Languages.Native.NativeLanguage ((ITargetInfo) inferior);

			sse.SymbolTableManager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);
		}

		public void ProcessEvent (long status)
		{
			Inferior.ChildEvent cevent = inferior.ProcessEvent (status);
			Report.Debug (DebugFlags.EventLoop,
				      "SSE {0} received event {1} ({2:x})",
				      this, cevent, status);
			if (sse.HandleChildEvent (inferior, cevent))
				return;
			ProcessEvent (cevent);
		}

		void send_frame_event (StackFrame frame, int signal)
		{
			SendTargetEvent (new TargetEventArgs (TargetEventType.TargetStopped, signal, frame));
		}

		void send_frame_event (StackFrame frame, BreakpointHandle handle)
		{
			SendTargetEvent (new TargetEventArgs (TargetEventType.TargetHitBreakpoint, handle, frame));
		}

		public void SendTargetEvent (TargetEventArgs args)
		{
			switch (args.Type) {
			case TargetEventType.TargetRunning:
				target_state = TargetState.RUNNING;
				break;

			case TargetEventType.TargetSignaled:
			case TargetEventType.TargetExited:
				target_state = TargetState.EXITED;
				OnTargetExitedEvent ();
				break;

			default:
				target_state = TargetState.STOPPED;
				break;
			}

			OnTargetEvent (args);
			sse.SetCompleted ();
		}

		public override void Start (TargetAddress func, bool is_main)
		{
			if (!func.IsNull) {
				insert_temporary_breakpoint (func);
				current_operation = StepOperation.Initialize;
				this.is_main = is_main;
			}
			do_continue ();
		}

		Inferior.ChildEvent wait ()
		{
			Inferior.ChildEvent child_event;
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
		// </summary>
		// <remarks>
		//   This method may only be used in the background thread.
		// </remarks>
		TargetEventArgs process_child_event (Inferior.ChildEvent child_event)
		{
			Inferior.ChildEventType message = child_event.Type;
			int arg = (int) child_event.Argument;

			if (stop_requested) {
				get_registers ();
				stop_event.Set ();
				restart_event.WaitOne ();
				// A stop was requested and we actually received the SIGSTOP.  Note that
				// we may also have stopped for another reason before receiving the SIGSTOP.
				if ((message == Inferior.ChildEventType.CHILD_STOPPED) && (arg == inferior.SIGSTOP))
					return null;
				// Ignore the next SIGSTOP.
				pending_sigstop++;
			}

			if ((message == Inferior.ChildEventType.CHILD_STOPPED) && (arg != 0)) {
				if ((pending_sigstop > 0) && (arg == inferior.SIGSTOP)) {
					--pending_sigstop;
					return null;
				}
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
				else if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
					if (arg == temp_breakpoint_id) {
						// we hit the temporary breakpoint; this'll always
						// happen in the `correct' thread since the
						// `temp_breakpoint_id' is only set in this
						// SingleSteppingEngine and not in any other thread's.
						message = Inferior.ChildEventType.CHILD_STOPPED;
						arg = 0;
					}
				}
			}

			if (message == Inferior.ChildEventType.CHILD_HIT_BREAKPOINT) {
				Inferior.ChildEvent new_event;
				// Ok, the next thing we need to check is whether this is actually "our"
				// breakpoint or whether it belongs to another thread.  In this case,
				// `step_over_breakpoint' does everything for us and we can just continue
				// execution.
				if (step_over_breakpoint (false, out new_event)) {
					int new_arg = (int) new_event.Argument;
					// If the child stopped normally, just continue its execution
					// here; otherwise, we need to deal with the unexpected stop.
					if ((new_event.Type != Inferior.ChildEventType.CHILD_STOPPED) ||
					    ((new_arg != 0) && (new_arg != inferior.SIGSTOP))) {
#if FIXME
						child_event = new_event;
						goto again;
#endif
					}
					return null;
				} else if (!child_breakpoint (arg)) {
					// we hit any breakpoint, but its handler told us
					// to resume the target and continue.
					return null;
				}
			}

			if (temp_breakpoint_id != 0) {
				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}

			switch (message) {
			case Inferior.ChildEventType.CHILD_STOPPED:
				if (arg != 0) {
					frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
					return new TargetEventArgs (TargetEventType.TargetStopped, arg, current_frame);
				}

				return null;

			case Inferior.ChildEventType.CHILD_HIT_BREAKPOINT:
				return null;

			case Inferior.ChildEventType.CHILD_SIGNALED:
				return new TargetEventArgs (TargetEventType.TargetSignaled, arg);

			case Inferior.ChildEventType.CHILD_EXITED:
				return new TargetEventArgs (TargetEventType.TargetExited, arg);

			case Inferior.ChildEventType.CHILD_CALLBACK:
				frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
				return new TargetEventArgs (TargetEventType.TargetStopped, arg, current_frame);
			}

			return null;
		}

		// <summary>
		//   The heart of the SingleSteppingEngine's background thread - process a
		//   command and send the result back to the caller.
		// </summary>
		public void ProcessCommand (Command command)
		{
			frames_invalid ();

			// Process another stepping command.
			switch (command.Operation) {
			case StepOperation.Run:
			case StepOperation.RunInBackground:
				TargetAddress until = command.Until;
				if (!until.IsNull)
					insert_temporary_breakpoint (until);
				do_continue ();
				break;

			case StepOperation.StepNativeInstruction:
				do_step ();
				break;

			case StepOperation.NextInstruction:
				do_next ();
				break;

			case StepOperation.RuntimeInvoke:
				do_runtime_invoke ((RuntimeInvokeData) command.CommandFuncData);
				break;

			default:
				Step (command.Operation, command.StepFrame);
				break;
			}
		}

		protected bool ProcessEvent (Inferior.ChildEvent cevent)
		{
			TargetEventArgs result = process_child_event (cevent);

		send_result:
			// If `result' is not null, then the target stopped abnormally.
			if (result != null) {
				if (DaemonEventHandler != null) {
					if (DaemonEventHandler (this, inferior, result))
						return false;
				}
				SendTargetEvent (result);
				return true;
			}

			if (!DoStep (false))
				return false;

			if (current_operation == StepOperation.Initialize) {
				if (is_main)
					sse.Initialize (inferior);
				step_operation_finished ();
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
				return false;
			}

			// `frame_changed' computes the new stack frame - and it may also
			// send us a new step command.  This happens for instance, if we
			// stopped within a method's prologue or epilogue code.
			Command new_command = frame_changed (frame, 0, current_operation);
			if (new_command != null) {
				Console.WriteLine ("NEW COMMAND: {0}", new_command);
				// return false;
			}

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
			frame_changed (inferior.CurrentFrame, 0, StepOperation.None);
			return CommandResult.Ok;
		}

		void update_symtabs (object sender, ISymbolTable symbol_table,
				     ISimpleSymbolTable simple_symtab)
		{
			disassembler.SymbolTable = simple_symtab;
			current_simple_symtab = simple_symtab;
			current_symtab = symbol_table;

			// send_sync_command (new CommandFunc (reload_symtab), null);
		}

		public IMethod Lookup (TargetAddress address)
		{
			if (current_symtab == null)
				return null;

			return current_symtab.Lookup (address);
		}

		public string SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (current_simple_symtab == null)
				return null;

			return current_simple_symtab.SimpleLookup (address, exact_match);
		}

		protected void get_registers ()
		{
			long[] regs = inferior.GetRegisters (arch.AllRegisterIndices);
			registers = new Register [regs.Length];
			for (int i = 0; i < regs.Length; i++)
				registers [i] = new Register (arch.AllRegisterIndices [i], regs [i]);
		}

		protected SingleSteppingEngine sse;
		protected Inferior inferior;
		protected IArchitecture arch;
		protected IDisassembler disassembler;
		ISymbolTable current_symtab;
		ISimpleSymbolTable current_simple_symtab;
		ILanguage native_language;
		protected AutoResetEvent stop_event;
		protected AutoResetEvent restart_event;
		protected bool stop_requested = false;
		int pending_sigstop = 0;
		bool is_main;
		bool native;
		protected int pid, tid;

		protected TargetAddress main_method_retaddr = TargetAddress.Null;
		TargetState target_state = TargetState.NO_TARGET;

		bool in_event = false;

		protected void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		public override IArchitecture Architecture {
			get { return arch; }
		}


		public ILanguage NativeLanguage {
			get {
				return native_language;
			}
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
			child_already_exited = true;

			inferior.Dispose ();
			inferior = null;
			frames_invalid ();
		}

		bool child_already_exited;
		bool debugger_info_read;

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
		bool child_breakpoint (int index)
		{
			// The inferior knows about breakpoints from all threads, so if this is
			// zero, then no other thread has set this breakpoint.
			if (index == 0)
				return true;

			BreakpointManager.Handle handle = sse.BreakpointManager.LookupBreakpoint (index);
			if (handle == null)
				return true;

			StackFrame frame = null;
			// Only compute the current stack frame if the handler actually
			// needs it.  Note that this computation is an expensive operation
			// so we should only do it when it's actually needed.
			if (handle.NeedsFrame)
				frame = get_frame (inferior.CurrentFrame);
			if ((handle.CheckHandler != null) &&
			    !handle.CheckHandler (frame, index, handle.UserData))
				return false;

			frame_changed (inferior.CurrentFrame, 0, current_operation);
			send_frame_event (current_frame, handle.BreakpointHandle);

			return true;
		}

		bool step_over_breakpoint (bool current, out Inferior.ChildEvent new_event)
		{
			int index;
			BreakpointManager.Handle handle = sse.BreakpointManager.LookupBreakpoint (
				inferior.CurrentFrame, out index);

			if (handle == null) {
				new_event = null;
				return false;
			}

			Console.WriteLine ("STEP OVER BREAKPOINT: {0} {1} {2}",
					   current, handle, index);

			if (!current && handle.BreakpointHandle.Breaks (this)) {
				new_event = null;
				return false;
			}

			sse.AcquireGlobalThreadLock (inferior, this);
			inferior.DisableBreakpoint (index);
			inferior.Step ();
			do {
				new_event = inferior.Wait ();
			} while (new_event == null);
			inferior.EnableBreakpoint (index);
			sse.ReleaseGlobalThreadLock (inferior, this);
			return true;
		}

		protected IMethod current_method;
		protected StackFrame current_frame;
		protected Backtrace current_backtrace;
		protected Register[] registers;

		// <summary>
		//   Compute the StackFrame for target address @address.
		// </summary>
		StackFrame get_frame (TargetAddress address)
		{
			// If we have a current_method and the address is still inside
			// that method, we don't need to do a method lookup.
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				sse.DebuggerBackend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			// This gets just one single stack frame.
			Inferior.StackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			if ((current_method != null) && current_method.HasSource) {
				SourceAddress source = current_method.Source.Lookup (address);

				current_frame = CreateFrame (
					address, 0, frames [0], null, source, current_method);
			} else
				current_frame = CreateFrame (
					address, 0, frames [0], null, null, null);

			return current_frame;
		}

		protected abstract StackFrame CreateFrame (TargetAddress address, int level,
							   Inferior.StackFrame frame,
							   Backtrace backtrace,
							   SourceAddress source,
							   IMethod method);

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
				sse.DebuggerBackend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			Inferior.StackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			// Compute the current stack frame.
			if ((current_method != null) && current_method.HasSource) {
				SourceAddress source = current_method.Source.Lookup (address);

				// If check_method_operation() returns true, it already
				// started a stepping operation, so the target is
				// currently running.
				Command new_command = check_method_operation (
					address, current_method, source, operation);
				if (new_command != null)
					return new_command;

				current_frame = CreateFrame (
					address, 0, frames [0], null, source, current_method);
			} else
				current_frame = CreateFrame (
					address, 0, frames [0], null, null, null);

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
						SourceAddress source, StepOperation operation)
		{
			if (operation == StepOperation.StepNativeInstruction)
				return null;

			if (method.IsWrapper && (address == method.StartAddress))
				return new Command (this, StepOperation.Run, method.WrapperAddress);

			ILanguageBackend language = method.Module.LanguageBackend as ILanguageBackend;
			if (source == null)
				return null;

			// Do nothing if this is not a source stepping operation.
			if ((operation != StepOperation.StepLine) &&
			    (operation != StepOperation.NextLine) &&
			    (operation != StepOperation.Run) &&
			    (operation != StepOperation.RunInBackground) &&
			    (operation != StepOperation.RuntimeInvoke) &&
			    (operation != StepOperation.Initialize))
				return null;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				// We stopped between two source lines.  This normally
				// happens when returning from a method call; in this
				// case, we need to continue stepping until we reach the
				// next source line.
				return new Command (this, StepOperation.Native, new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					language, operation == StepOperation.StepLine ?
					StepMode.StepFrame : StepMode.Finish));
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				// Do not stop inside a method's prologue code, but stop
				// immediately behind it (on the first instruction of the
				// method's actual code).
				return new Command (this, StepOperation.Native, new StepFrame (
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
		void do_step ()
		{
			check_inferior ();
			do_continue_internal (false);
		}

		// <summary>
		//   Step over the next machine instruction.
		// </summary>
		void do_next ()
		{
			check_inferior ();
			TargetAddress address = inferior.CurrentFrame;

			// Check whether this is a call instruction.
			int insn_size;
			TargetAddress call = arch.GetCallTarget (inferior, address, out insn_size);
			if (call.IsNull) {
				// Step one instruction unless this is a call
				do_step ();
				return;
			}

			// Insert a temporary breakpoint immediately behind it and continue.
			address += insn_size;
			insert_temporary_breakpoint (address);
			do_continue ();
		}

		// <summary>
		//   Resume the target.  If @is_breakpoint is true, the current
		//   instruction is a breakpoint; in this case we need to step one
		//   instruction before we can resume the target (see `must_continue' in
		//   child_event() for more info).
		// </summary>
		void do_continue ()
		{
			check_inferior ();
			do_continue_internal (true);
		}

		void do_continue_internal (bool do_run)
		{
			check_inferior ();

			Inferior.ChildEvent new_event;
			if (step_over_breakpoint (true, out new_event)) {
				int new_arg = (int) new_event.Argument;
				// If the child stopped normally, just continue its execution
				// here; otherwise, we need to deal with the unexpected stop.
#if FIXME
				if ((new_event.Type != Inferior.ChildEventType.CHILD_STOPPED) ||
				    ((new_arg != 0) && (new_arg != inferior.SIGSTOP)))
					return new_event;
				else if (!do_run)
					return new_event;
#endif
			}

			if (do_run)
				inferior.Continue ();
			else
				inferior.Step ();
		}

		StepFrame current_operation_frame;
		StepOperation current_operation;

		protected bool Step (StepOperation operation, StepFrame frame)
		{
			check_inferior ();

			current_operation = StepOperation.None;
			current_operation_frame = null;

			/*
			 * If no step frame is given, just step one machine instruction.
			 */
			if (frame == null) {
				do_step ();
				return true;
			}

			/*
			 * Step one instruction, but step over function calls.
			 */
			if (frame.Mode == StepMode.NextInstruction) {
				do_next ();
				return true;
			}

			current_operation = operation;
			current_operation_frame = frame;
			if (DoStep (true)) {
				step_operation_finished ();
				return true;
			}
			return false;
		}

		void step_operation_finished ()
		{
			current_operation = StepOperation.None;
			current_operation_frame = null;
		}

		protected bool DoStep (bool first)
		{
			StepFrame frame = current_operation_frame;
			if (frame == null)
				return true;

			TargetAddress current_frame = inferior.CurrentFrame;
			if (!first && !is_in_step_frame (frame, current_frame))
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
				TargetAddress trampoline = frame.Language.GetTrampoline (inferior, call);
				IMethod tmethod = null;

				/*
				 * If this is a trampoline, insert a breakpoint at the start of
				 * the corresponding method and continue.
				 *
				 * We don't need to distinguish between StepMode.SingleInstruction
				 * and StepMode.StepFrame here since we'd leave the step frame anyways
				 * when entering the method.
				 */
				if (!trampoline.IsNull) {
					if (current_symtab != null) {
						sse.DebuggerBackend.UpdateSymbolTable ();
						tmethod = Lookup (trampoline);
					}
					if ((tmethod == null) || !tmethod.Module.StepInto) {
						do_next ();
						return false;
					}

					insert_temporary_breakpoint (trampoline);
					do_continue ();
					return true;
				}

				if (frame.Mode != StepMode.SingleInstruction) {
					/*
					 * If this is an ordinary method, check whether we have
					 * debugging info for it and don't step into it if not.
					 */
					tmethod = Lookup (call);
					if ((tmethod == null) || !tmethod.Module.StepInto) {
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
				return true;
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
			if ((method == null) || !method.Module.StepInto) {
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

				if ((wmethod == null) || !wmethod.Module.StepInto) {
					do_next ();
					return false;
				}

				insert_temporary_breakpoint (wrapper);
				do_continue ();
				return true;
			}

			/*
			 * Finally, step into the method.
			 */
			do_step ();
			return true;
		}

		// <summary>
		//   Create a step frame to step until the next source line.
		// </summary>
		protected StepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
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
		protected StepFrame get_simple_step_frame (StepMode mode)
		{
			check_inferior ();
			object language;

			if (current_method != null)
				language = current_method.Module.LanguageBackend;
			else
				language = null;

			return new StepFrame (language, mode);
		}

		void do_runtime_invoke (RuntimeInvokeData rdata)
		{
			check_inferior ();

			TargetAddress invoke = rdata.Language.CompileMethod (inferior, rdata.MethodArgument);

			insert_temporary_breakpoint (invoke);

			inferior.RuntimeInvoke (
				rdata.Language.RuntimeInvokeFunc, rdata.MethodArgument, rdata.ObjectArgument, rdata.ParamObjects);

			do_continue ();
		}

		protected struct RuntimeInvokeData
		{
			public readonly TargetAddress InvokeMethod;
			public readonly ILanguageBackend Language;
			public readonly TargetAddress MethodArgument;
			public readonly TargetAddress ObjectArgument;
			public readonly TargetAddress[] ParamObjects;

			public RuntimeInvokeData (ILanguageBackend language, TargetAddress method_argument,
						  TargetAddress object_argument, TargetAddress[] param_objects)
			{
				this.Language = language;
				this.InvokeMethod = TargetAddress.Null;
				this.MethodArgument = method_argument;
				this.ObjectArgument = object_argument;
				this.ParamObjects = param_objects;
			}

			public RuntimeInvokeData (TargetAddress invoke_method, TargetAddress method_argument,
						  TargetAddress object_argument, TargetAddress[] param_objects)
			{
				this.Language = null;
				this.InvokeMethod = invoke_method;
				this.MethodArgument = method_argument;
				this.ObjectArgument = object_argument;
				this.ParamObjects = param_objects;
			}
		}

		//
		// IDisposable
		//

		protected override void DoDispose ()
		{
			if (inferior != null)
				inferior.Kill ();
			inferior = null;
		}
	}

	protected class EngineProcess : TheEngine, ITargetAccess, IDisassembler
	{
		public EngineProcess (SingleSteppingEngine sse, ProcessStart start)
			: base (sse, start)
		{ }

		public EngineProcess (SingleSteppingEngine sse, Inferior inferior, int pid)
			: base (sse, inferior, pid)
		{
		}

		// <summary>
		//   The single-stepping engine's target state.  This will be
		//   TargetState.RUNNING while the engine is stepping.
		// </summary>
		public override TargetState State {
			get {
				return inferior.State;
			}
		}

		public override int PID {
			get {
				return pid;
			}
		}

		public override int TID {
			get {
				return tid;
			}
		}

		internal Inferior TheInferior {
			get {
				return inferior;
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
		public override StackFrame CurrentFrame {
			get {
				check_inferior ();
				return current_frame;
			}
		}

		public override TargetAddress CurrentFrameAddress {
			get {
				StackFrame frame = CurrentFrame;
				return frame != null ? frame.TargetAddress : TargetAddress.Null;
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
		public override Backtrace GetBacktrace ()
		{
			check_inferior ();

			if (current_backtrace != null)
				return current_backtrace;

			sse.SendSyncCommand (new CommandFunc (get_backtrace), -1);

			return current_backtrace;
		}

		public override Backtrace GetBacktrace (int max_frames)
		{
			check_inferior ();

			if ((max_frames == -1) && (current_backtrace != null))
				return current_backtrace;

			sse.SendSyncCommand (new CommandFunc (get_backtrace), max_frames);

			return current_backtrace;
		}

		CommandResult get_backtrace (object data)
		{
			sse.DebuggerBackend.UpdateSymbolTable ();

			Inferior.StackFrame[] iframes = inferior.GetBacktrace ((int) data, main_method_retaddr);
			StackFrame[] frames = new StackFrame [iframes.Length];
			MyBacktrace backtrace = new MyBacktrace (this);

			for (int i = 0; i < iframes.Length; i++) {
				TargetAddress address = iframes [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceAddress source = method.Source.Lookup (address);
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i], backtrace, source, method);
				} else
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i], backtrace);
			}

			backtrace.SetFrames (frames);
			current_backtrace = backtrace;
			return CommandResult.Ok;
		}

		public override long GetRegister (int index)
		{
			foreach (Register register in GetRegisters ()) {
				if (register.Index == index)
					return (long) register.Data;
			}

			throw new NoSuchRegisterException ();
		}

		public override Register[] GetRegisters ()
		{
			check_inferior ();

			if (registers != null)
				return registers;

			sse.SendSyncCommand (new CommandFunc (get_registers), null);

			return registers;
		}

		CommandResult get_registers (object data)
		{
			get_registers ();
			return CommandResult.Ok;
		}

		CommandResult set_register (object data)
		{
			Register reg = (Register) data;
			inferior.SetRegister (reg.Index, (long) reg.Data);
			registers = null;
			return CommandResult.Ok;
		}

		public override void SetRegister (int register, long value)
		{
			Register reg = new Register (register, value);
			sse.SendSyncCommand (new CommandFunc (set_register), reg);
		}

		public override void SetRegisters (int[] registers, long[] values)
		{
			throw new NotImplementedException ();
		}

		public override TargetMemoryArea[] GetMemoryMaps ()
		{
			check_inferior ();
			return inferior.GetMemoryMaps ();
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

		bool start_step_operation (Command command, bool wait)
		{
			check_inferior ();
			if (!sse.CheckCanRun ())
				return false;
			sse.SendAsyncCommand (command, wait);
			return true;
		}

		bool start_step_operation (StepOperation operation, StepFrame frame,
					   bool wait)
		{
			return start_step_operation (new Command (this, operation, frame), wait);
		}

		bool start_step_operation (StepOperation operation, TargetAddress until,
					   bool wait)
		{
			return start_step_operation (new Command (this, operation, until), wait);
		}

		bool start_step_operation (StepOperation operation, bool wait)
		{
			return start_step_operation (new Command (this, operation), wait);
		}

		bool start_step_operation (StepOperation operation, object data, bool wait)
		{
			return start_step_operation (new Command (this, operation, data), wait);
		}

		bool start_step_operation (StepMode mode, bool wait)
		{
			StepFrame frame = get_simple_step_frame (mode);
			return start_step_operation (StepOperation.Native, frame, wait);
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public override bool StepInstruction (bool wait)
		{
			return start_step_operation (StepMode.SingleInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public override bool StepNativeInstruction (bool wait)
		{
			return start_step_operation (StepOperation.StepNativeInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public override bool NextInstruction (bool wait)
		{
			return start_step_operation (StepOperation.NextInstruction, wait);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public override bool StepLine (bool wait)
		{
			return start_step_operation (StepOperation.StepLine,
						     get_step_frame (), wait);
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public override bool NextLine (bool wait)
		{
			check_inferior ();
			if (!sse.CheckCanRun ())
				return false;

			Command command;
			StepFrame new_frame, frame = get_step_frame ();
			if (frame == null) {
				new_frame = get_simple_step_frame (StepMode.NextInstruction);

				command = new Command (this, StepOperation.Native, new_frame);
			} else {
				new_frame = new StepFrame (
					frame.Start, frame.End, null, StepMode.Finish);

				command = new Command (this, StepOperation.NextLine, new_frame);
			}

			sse.SendAsyncCommand (command, wait);
			return true;
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public override bool Finish (bool wait)
		{
			check_inferior ();
			if (!sse.CheckCanRun ())
				return false;

			StackFrame frame = CurrentFrame;
			if (frame.Method == null) {
				sse.ReleaseCommandMutex ();
				throw new NoMethodException ();
			}

			StepFrame sf = new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress,
				null, StepMode.Finish);

			Command command = new Command (this, StepOperation.StepFrame, sf);
			sse.SendAsyncCommand (command, wait);
			return true;
		}

		public override bool Continue (TargetAddress until, bool in_background, bool wait)
		{
			if (in_background)
				return start_step_operation (StepOperation.RunInBackground,
							     until, wait);
			else
				return start_step_operation (StepOperation.Run, until, wait);
		}

		public override void Stop ()
		{
			// Try to get the command mutex; if we succeed, then no stepping
			// operation is currently running.
			check_inferior ();
			bool stopped = sse.CheckCanRun ();
			Console.WriteLine ("STOP: {0} {1} {2}", this, stopped, State);
			if (stopped) {
				inferior.Stop ();
				sse.ReleaseCommandMutex ();
				return;
			}

			// Ok, there's an operation running.
			// Stop the inferior and wait until the currently running operation
			// completed.
			inferior.Stop ();
			sse.WaitForCompletion (true);
		}

		public override void Kill ()
		{
			Dispose ();
		}

#if FIXME
		public void SetSignal (int signal, bool send_it)
		{
			// Try to get the command mutex; if we succeed, then no stepping operation
			// is currently running.
			check_inferior ();
			bool stopped = sse.CheckCanRun ();
			if (stopped) {
				inferior.SetSignal (signal, send_it);
				sse.ReleaseCommandMutex ();
				return;
			}

			throw new TargetNotStoppedException ();
		}
#endif

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		public override Register[] AcquireThreadLock ()
		{
			// Try to get the command mutex; if we succeed, then no stepping operation
			// is currently running.
			check_inferior ();
			bool stopped = sse.CheckCanRun ();
			if (stopped)
				return GetRegisters ();

			// Ok, there's an operation running.  Stop the inferior and wait until the
			// currently running operation completed.
			stop_requested = true;
			inferior.Stop ();
			stop_event.WaitOne ();
			return registers;
		}

		public override void ReleaseThreadLock ()
		{
			lock (this) {
				if (stop_requested) {
					stop_requested = false;
					sse.ResetCompleted ();
					restart_event.Set ();
				}
				sse.ReleaseCommandMutex ();
			}
		}

		CommandResult insert_breakpoint (object data)
		{
			int index = sse.BreakpointManager.InsertBreakpoint (
				inferior, (BreakpointManager.Handle) data);
			return new CommandResult (CommandResultType.CommandOk, index);
		}

		CommandResult remove_breakpoint (object data)
		{
			sse.BreakpointManager.RemoveBreakpoint (inferior, (int) data);
			return CommandResult.Ok;
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
		public override int InsertBreakpoint (BreakpointHandle handle,
						      TargetAddress address,
						      BreakpointCheckHandler check_handler,
						      BreakpointHitHandler hit_handler,
						      bool needs_frame, object user_data)
		{
			check_inferior ();

			BreakpointManager.Handle data = new BreakpointManager.Handle (
				address, handle, check_handler, hit_handler, needs_frame, user_data);

			CommandResult result = sse.SendSyncCommand (new CommandFunc (insert_breakpoint), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();

			return (int) result.Data;
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		public override void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (inferior != null)
				sse.SendSyncCommand (new CommandFunc (remove_breakpoint), index);
		}

		//
		// Disassembling.
		//

		public override IDisassembler Disassembler {
			get { return this; }
		}

		ISimpleSymbolTable IDisassembler.SymbolTable {
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

		CommandResult get_insn_size (object data)
		{
			lock (disassembler) {
				TargetAddress address = (TargetAddress) data;
				int result = disassembler.GetInstructionSize (address);
				return new CommandResult (CommandResultType.CommandOk, result);
			}
		}

		public int GetInstructionSize (TargetAddress address)
		{
			check_inferior ();
			CommandResult result = sse.SendSyncCommand (new CommandFunc (get_insn_size), address);
			if (result.Type == CommandResultType.CommandOk) {
				return (int) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		private struct DisassembleData
		{
			public readonly IMethod Method;
			public readonly TargetAddress Address;

			public DisassembleData (IMethod method, TargetAddress address)
			{
				this.Method = method;
				this.Address = address;
			}
		}

		CommandResult disassemble_insn (object data)
		{
			lock (disassembler) {
				DisassembleData dis = (DisassembleData) data;
				AssemblerLine result = disassembler.DisassembleInstruction (
					dis.Method, dis.Address);
				return new CommandResult (CommandResultType.CommandOk, result);
			}
		}

		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			check_inferior ();
			DisassembleData data = new DisassembleData (method, address);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (disassemble_insn), data);
			if (result.Type == CommandResultType.CommandOk) {
				return (AssemblerLine) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				return null;
		}

		CommandResult disassemble_method (object data)
		{
			lock (disassembler) {
				AssemblerMethod block = disassembler.DisassembleMethod ((IMethod) data);
				return new CommandResult (CommandResultType.CommandOk, block);
			}
		}

		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			check_inferior ();
			CommandResult result = sse.SendSyncCommand (new CommandFunc (disassemble_method), method);
			if (result.Type == CommandResultType.CommandOk)
				return (AssemblerMethod) result.Data;
			else if (result.Type == CommandResultType.Exception)
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
			public readonly long Argument1;
			public readonly long Argument2;
			public readonly string StringArgument;

			public CallMethodData (TargetAddress method, long arg, string string_arg)
			{
				this.Method = method;
				this.Argument1 = arg;
				this.Argument2 = 0;
				this.StringArgument = string_arg;
			}

			public CallMethodData (TargetAddress method, long arg1, long arg2)
			{
				this.Method = method;
				this.Argument1 = arg1;
				this.Argument2 = arg2;
				this.StringArgument = null;
			}
		}

		CommandResult call_string_method (object data)
		{
			CallMethodData cdata = (CallMethodData) data;
			long retval = inferior.CallStringMethod (cdata.Method, cdata.Argument1, cdata.StringArgument);
			return new CommandResult (CommandResultType.CommandOk, retval);
		}

		public override long CallMethod (TargetAddress method, long method_argument,
						 string string_argument)
		{
			CallMethodData data = new CallMethodData (method, method_argument, string_argument);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (call_string_method), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();
			return (long) result.Data;
		}

		public TargetAddress CallMethod (TargetAddress method, string string_argument)
		{
			CallMethodData data = new CallMethodData (method, 0, string_argument);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (call_string_method), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();
			long retval = (long) result.Data;
			if (inferior.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (inferior.AddressDomain, retval);
		}

		CommandResult call_method (object data)
		{
			CallMethodData cdata = (CallMethodData) data;
			long retval = inferior.CallMethod (cdata.Method, cdata.Argument1, cdata.Argument2);
			return new CommandResult (CommandResultType.CommandOk, retval);
		}

		internal long CallMethod (TargetAddress method, long arg1, long arg2)
		{
			CallMethodData data = new CallMethodData (method, arg1, arg2);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (call_method), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();
			return (long) result.Data;
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1, TargetAddress arg2)
		{
			CallMethodData data = new CallMethodData (method, arg1.Address, arg2.Address);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (call_method), data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();
			long retval = (long) result.Data;
			if (inferior.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (inferior.AddressDomain, retval);
		}

		CommandResult runtime_invoke_func (object data)
		{
			RuntimeInvokeData rdata = (RuntimeInvokeData) data;
			TargetAddress exc_object;
			TargetAddress retval = inferior.RuntimeInvoke (
				rdata.InvokeMethod, rdata.MethodArgument, rdata.ObjectArgument, rdata.ParamObjects,
				out exc_object);
			RuntimeInvokeResult result = new RuntimeInvokeResult (retval, exc_object);
			return new CommandResult (CommandResultType.CommandOk, result);
		}

		private struct RuntimeInvokeResult
		{
			public readonly TargetAddress ReturnObject;
			public readonly TargetAddress ExceptionObject;

			public RuntimeInvokeResult (TargetAddress return_object, TargetAddress exc_object)
			{
				this.ReturnObject = return_object;
				this.ExceptionObject = exc_object;
			}
		}

		protected bool RuntimeInvoke (StackFrame frame, TargetAddress method_argument, TargetAddress object_argument,
					      TargetAddress[] param_objects)
		{
			if ((frame == null) || (frame.Method == null))
				throw new ArgumentException ();
			ILanguageBackend language = frame.Method.Module.LanguageBackend as ILanguageBackend;
			if (language == null)
				throw new ArgumentException ();

			RuntimeInvokeData data = new RuntimeInvokeData (language, method_argument, object_argument, param_objects);
			return start_step_operation (StepOperation.RuntimeInvoke, data, true);
		}

		TargetAddress ITargetAccess.RuntimeInvoke (TargetAddress invoke_method, TargetAddress method_argument,
							   TargetAddress object_argument, TargetAddress[] param_objects,
							   out TargetAddress exc_object)
		{
			RuntimeInvokeData data = new RuntimeInvokeData (invoke_method, method_argument, object_argument, param_objects);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (runtime_invoke_func), data);
			if (result.Type == CommandResultType.CommandOk) {
				RuntimeInvokeResult retval = (RuntimeInvokeResult) result.Data;
				exc_object = retval.ExceptionObject;
				return retval.ReturnObject;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new Exception ();
		}

		public override bool HasTarget {
			get { return inferior != null; }
		}

		public override bool CanRun {
			get { return true; }
		}

		public override bool CanStep {
			get { return true; }
		}

		public override bool IsStopped {
			get { return State == TargetState.STOPPED; }
		}

		public override ITargetAccess TargetAccess {
			get { return this; }
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get { return this; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return this; }
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

			byte[] buffer = inferior.ReadBuffer (mdata.Address, mdata.Size);
			return new CommandResult (CommandResultType.CommandOk, buffer);
		}

		protected byte[] read_memory (TargetAddress address, int size)
		{
			ReadMemoryData data = new ReadMemoryData (address, size);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (do_read_memory), data);
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

			string retval = inferior.ReadString (mdata.Address);
			return new CommandResult (CommandResultType.CommandOk, retval);
		}

		string read_string (TargetAddress address)
		{
			ReadMemoryData data = new ReadMemoryData (address, 0);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (do_read_string), data);
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

		private struct WriteMemoryData
		{
			public TargetAddress Address;
			public byte[] Data;

			public WriteMemoryData (TargetAddress address, byte[] data)
			{
				this.Address = address;
				this.Data = data;
			}
		}

		CommandResult do_write_memory (object data)
		{
			WriteMemoryData mdata = (WriteMemoryData) data;

			inferior.WriteBuffer (mdata.Address, mdata.Data);
			return CommandResult.Ok;
		}

		protected void write_memory (TargetAddress address, byte[] buffer)
		{
			WriteMemoryData data = new WriteMemoryData (address, buffer);
			CommandResult result = sse.SendSyncCommand (new CommandFunc (do_write_memory), data);
			if (result.Type == CommandResultType.CommandOk)
				return;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		AddressDomain ITargetMemoryInfo.AddressDomain {
			get {
				return inferior.AddressDomain;
			}
		}

		AddressDomain ITargetMemoryInfo.GlobalAddressDomain {
			get {
				return sse.AddressDomain;
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

		void ITargetMemoryAccess.WriteBuffer (TargetAddress address, byte[] buffer)
		{
			write_memory (address, buffer);
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
			TargetBinaryWriter writer = new TargetBinaryWriter (TargetAddressSize, this);
			writer.WriteAddress (value);
			write_memory (address, writer.Contents);
		}


		//
		// Stack frames.
		//

		protected override StackFrame CreateFrame (TargetAddress address, int level,
							   Inferior.StackFrame frame,
							   Backtrace bt, SourceAddress source,
							   IMethod method)
		{
			if (source != null)
				return new MyStackFrame (this, address, level, frame,
							 bt, source, method);
			else
				return new MyStackFrame (this, address, level, frame, bt);
		}

		protected class MyStackFrame : StackFrame
		{
			EngineProcess sse;
			Inferior.StackFrame frame;
			Backtrace backtrace;
			ILanguage language;

			Register[] registers;
			bool has_registers;

			public MyStackFrame (EngineProcess sse, TargetAddress address, int level,
					     Inferior.StackFrame frame, Backtrace backtrace,
					     SourceAddress source, IMethod method)
				: base (address, level, source, method)
			{
				this.sse = sse;
				this.frame = frame;
				this.backtrace = backtrace;
				this.language = method.Module.Language;
			}

			public MyStackFrame (EngineProcess sse, TargetAddress address, int level,
					     Inferior.StackFrame frame, Backtrace backtrace)
				: base (address, level, sse.SimpleLookup (address, false))
			{
				this.sse = sse;
				this.frame = frame;
				this.backtrace = backtrace;
				this.language = sse.NativeLanguage;
			}

			public override ITargetAccess TargetAccess {
				get { return sse; }
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

			public override TargetLocation GetRegisterLocation (int index, long reg_offset, bool dereference, long offset)
			{
				return new MonoVariableLocation (this, dereference, index, reg_offset, false, offset);
			}

			public override void SetRegister (int index, long value)
			{
				if (backtrace != null)
					throw new NotImplementedException ();

				sse.SetRegister (index, value);

				has_registers = false;
				registers = null;
			}

			public override ILanguage Language {
				get {
					return language;
				}
			}

			protected override AssemblerLine DoDisassembleInstruction (TargetAddress address)
			{
				return sse.DisassembleInstruction (Method, address);
			}

			public override AssemblerMethod DisassembleMethod ()
			{
				if (Method == null)
					throw new NoMethodException ();

				return sse.DisassembleMethod (Method);
			}

			public override bool RuntimeInvoke (TargetAddress method_argument, TargetAddress object_argument,
							    TargetAddress[] param_objects)
			{
				return sse.RuntimeInvoke (this, method_argument, object_argument, param_objects);
			}
		}

		//
		// Backtrace.
		//

		protected class MyBacktrace : Backtrace
		{
			public MyBacktrace (EngineProcess sse, StackFrame[] frames)
				: base (sse, frames)
			{
			}

			public MyBacktrace (EngineProcess sse)
				: this (sse, null)
			{ }

			public void SetFrames (StackFrame[] frames)
			{
				this.frames = frames;
			}
		}
	}

	//
	// IDisposable
	//

	protected override void DoDispose ()
	{
		if (inferior_thread != null) {
			lock (this) {
				abort_requested = true;
				engine_event.Set ();
			}
			inferior_thread.Join ();
		}

		if (wait_thread != null)
			wait_thread.Abort ();

		TheEngine[] threads = new TheEngine [thread_hash.Count];
		thread_hash.Values.CopyTo (threads, 0);
		for (int i = 0; i < threads.Length; i++)
			threads [i].Dispose ();
	}
}
}
