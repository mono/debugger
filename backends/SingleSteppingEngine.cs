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

		start_event = new ManualResetEvent (false);
		completed_event = new AutoResetEvent (false);
		command_mutex = new Mutex ();

		ready_event = new ManualResetEvent (false);
	}

	EngineProcess the_engine;
	protected readonly SymbolTableManager SymbolTableManager;

	ProcessStart start;
	Thread inferior_thread;
	ManualResetEvent ready_event;
	Hashtable thread_hash;

	int thread_lock_level;
	Mutex thread_lock_mutex;
	AddressDomain address_domain;
	ThreadGroup global_group;

	ManualResetEvent start_event;
	AutoResetEvent completed_event;
	Mutex command_mutex;
	bool sync_command_running;
	bool abort_requested;

	[DllImport("monodebuggerserver")]
	static extern int mono_debugger_server_global_wait (out long status, out int aborted);

	[DllImport("monodebuggerserver")]
	static extern void mono_debugger_server_abort_wait ();

	void start_inferior ()
	{
		the_engine = new EngineProcess (this, start);

		Report.Debug (DebugFlags.Threads, "Engine started: {0}", the_engine.PID);

		thread_hash.Add (the_engine.PID, the_engine);

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
	TheEngine command_engine = null;

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

		inferior_thread = new Thread (new ThreadStart (start_inferior));
		inferior_thread.Start ();

		ready_event.WaitOne ();

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
	}

	protected void ReachedMain ()
	{
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
		thread_lock_mutex.WaitOne ();
		Report.Debug (DebugFlags.Threads, "Acquiring global thread lock: {0} {1}",
			      caller, thread_lock_level);
		if (thread_lock_level++ > 0)
			return;
		foreach (EngineProcess engine in thread_hash.Values) {
			if (engine == caller)
				continue;
			Register[] regs = engine.AcquireThreadLock ();
			long esp = (long) regs [(int) I386Register.ESP].Data;
			TargetAddress addr = new TargetAddress (inferior.AddressDomain, esp);
			Report.Debug (DebugFlags.Threads, "Got thread lock on {0}: {1}",
				      engine, addr);
		}
		Report.Debug (DebugFlags.Threads, "Done acquiring global thread lock: {0}",
			      caller);
	}

	internal override void ReleaseGlobalThreadLock (Inferior inferior, Process caller)
	{
		Report.Debug (DebugFlags.Threads, "Releasing global thread lock: {0} {1}",
			      caller, thread_lock_level);
		if (--thread_lock_level > 0) {
			thread_lock_mutex.ReleaseMutex ();
			return;
		}
				
		foreach (EngineProcess engine in thread_hash.Values) {
			if (engine == caller)
				continue;
			engine.ReleaseThreadLock ();
		}
		thread_lock_mutex.ReleaseMutex ();
		Report.Debug (DebugFlags.Threads, "Released global thread lock: {0}", caller);
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
		    mono_manager.ThreadCreated (new_thread, new_inferior, inferior)) {
			main_process = new_thread;

			main_method = mono_manager.Initialize (the_engine, inferior);

			Report.Debug (DebugFlags.Threads, "Managed main address is {0}",
				      main_method);

			new_thread.Start (main_method, true);
		}

		new_inferior.Continue ();
		OnThreadCreatedEvent (new_thread);

		inferior.Continue ();
	}

	internal override bool HandleChildEvent (Inferior inferior,
						 Inferior.ChildEvent cevent)
	{
		if (cevent.Type == Inferior.ChildEventType.NONE) {
			inferior.Continue ();
			return true;
		}

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
	//   The 'command_mutex' is used to protect the engine's main loop.
	//
	//   Before sending any command to it, you must acquire the mutex
	//   and release it when you're done with the command.
	//
	//   Note that you must not keep this mutex when returning from the
	//   function which acquired it.
	// </summary>
	protected bool AcquireCommandMutex (TheEngine engine)
	{
		if (!command_mutex.WaitOne (0, false))
			return false;

		command_engine = engine;
		return true;
	}

	protected void ReleaseCommandMutex ()
	{
		command_engine = null;
		command_mutex.ReleaseMutex ();
	}

	// <summary>
	//   Sends a synchronous command to the background thread and wait until
	//   it is completed.  This command never throws any exceptions, but returns
	//   an appropriate CommandResult if something went wrong.
	//
	//   This is used for non-steping commands such as getting a backtrace.
	// </summary>
	// <remarks>
	//   You must own either the 'command_mutex' or the `this' lock prior to
	//   calling this and you must make sure you aren't currently running any
	//   async operations.
	// </remarks>
	protected CommandResult SendSyncCommand (Command command)
	{
		if (Thread.CurrentThread == inferior_thread) {
			try {
				return command.Process.ProcessCommand (command);
			} catch (ThreadAbortException) {
				;
			} catch (Exception e) {
				return new CommandResult (e);
			}
		}

		if (!AcquireCommandMutex (null))
			return CommandResult.Busy;

		lock (this) {
			current_command = command;
			mono_debugger_server_abort_wait ();
			completed_event.Reset ();
			sync_command_running = true;
		}

		completed_event.WaitOne ();

		CommandResult result;
		lock (this) {
			result = command_result;
			command_result = null;
			current_command = null;
		}

		command_mutex.ReleaseMutex ();
		if (result != null)
			return result;
		else
			return new CommandResult (CommandResultType.UnknownError, null);
	}

	// <summary>
	//   Sends an asynchronous command to the background thread.  This is used
	//   for all stepping commands, no matter whether the user requested a
	//   synchronous or asynchronous operation.
	// </summary>
	// <remarks>
	//   You must own the 'command_mutex' before calling this method and you must
	//   make sure you aren't currently running any async commands.
	// </remarks>
	protected void SendAsyncCommand (Command command)
	{
		lock (this) {
			current_command = command;
			mono_debugger_server_abort_wait ();
		}
	}

	// <summary>
	//   The heart of the SingleSteppingEngine.  This runs in a background
	//   thread and processes stepping commands and events.
	//
	//   For each application we're debugging, there is just one SingleSteppingEngine,
	//   no matter how many threads the application has.  The engine is using one single
	//   event loop which is processing commands from the user and events from all of
	//   the application's threads.
	// </summary>
	void engine_thread_main ()
	{
		Report.Debug (DebugFlags.Wait, "SSE waiting");

		//
		// Wait until we got an event from the target or a command from the user.
		//

		int pid;
		int aborted;
		long status;
		pid = mono_debugger_server_global_wait (out status, out aborted);

		//
		// Note: `pid' is basically just an unique number which identifies the
		//       EngineProcess of this event.
		//

		if (pid > 0) {
			Report.Debug (DebugFlags.Wait,
				      "SSE received event: {0} {1:x}", pid, status);

			EngineProcess event_engine = (EngineProcess) thread_hash [pid];
			if (event_engine == null)
				throw new InternalError ("Got event {0:x} for unknown pid {1}",
							 status, pid);

			try {
				event_engine.ProcessEvent (status);
			} catch (ThreadAbortException) {
				;
			} catch (Exception e) {
				Console.WriteLine ("EXCEPTION: {0}", e);
			}

			if (!engine_is_ready) {
				engine_is_ready = true;
				start_event.Set ();
			}
		}

		//
		// We caught a SIGINT.
		//

		if (aborted > 0) {
			Report.Debug (DebugFlags.EventLoop, "SSE received SIGINT: {0} {1}",
				      command_engine, sync_command_running);

			lock (this) {
				if (sync_command_running) {
					command_result = CommandResult.Interrupted;
					current_command = null;
					sync_command_running = false;
					completed_event.Set ();
					return;
				}

				if (command_engine != null)
					command_engine.Interrupt ();
			}
			return;
		}

		if (abort_requested) {
			Report.Debug (DebugFlags.Wait, "Abort requested");
			return;
		}

		Command command;
		lock (this) {
			command = current_command;
			current_command = null;

			if (command == null)
				return;
		}

		if (command == null)
			return;

		Report.Debug (DebugFlags.EventLoop, "SSE received command: {0}", command);

		// These are synchronous commands; ie. the caller blocks on us
		// until we finished the command and sent the result.
		if (command.Type != CommandType.Operation) {
			CommandResult result;
			try {
				result = command.Process.ProcessCommand (command);
			} catch (ThreadAbortException) {
				;
				return;
			} catch (Exception e) {
				result = new CommandResult (e);
			}

			lock (this) {
				command_result = result;
				current_command = null;
				sync_command_running = false;
				completed_event.Set ();
			}
		} else {
			try {
				command.Process.ProcessCommand (command.Operation);
			} catch (ThreadAbortException) {
				return;
			} catch (Exception e) {
				Console.WriteLine ("EXCEPTION: {0} {1}", command, e);
			}
		}
	}

	public override AddressDomain AddressDomain {
		get { return address_domain; }
	}

	protected sealed class Operation {
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

	protected enum OperationType {
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
		CallMethod
	}

	protected enum CommandType {
		Operation,
		GetBacktrace,
		SetRegister,
		InsertBreakpoint,
		RemoveBreakpoint,
		GetInstructionSize,
		DisassembleInstruction,
		DisassembleMethod,
		ReadMemory,
		ReadString,
		WriteMemory
	}

	protected class Command {
		public TheEngine Process;
		public CommandType Type;
		public Operation Operation;
		public object Data1, Data2;

		public Command (TheEngine process, Operation operation)
		{
			this.Process = process;
			this.Type = CommandType.Operation;
			this.Operation = operation;
		}

		public Command (TheEngine process, CommandType type, object data, object data2)
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

	protected enum CommandResultType {
		ChildEvent,
		CommandOk,
		NotStopped,
		Interrupted,
		UnknownError,
		Exception
	}

	protected class CommandResult {
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

	// <summary>
	//   The SingleSteppingEngine creates one TheEngine instance for each thread
	//   in the target.
	//
	//   The `TheEngine' class is basically just responsible for whatever happens
	//   in the background thread: processing commands and events.  Their methods
	//   are just meant to be called from the SingleSteppingEngine (since it's a
	//   protected nested class they can't actually be called from anywhere else).
	//
	//   See the `EngineProcess' class for the "user interface".
	// </summary>
	protected abstract class TheEngine : NativeProcess
	{
		protected TheEngine (SingleSteppingEngine sse, Inferior inferior)
			: base (inferior.ProcessStart)
		{
			this.sse = sse;
			this.inferior = inferior;
			this.target = (EngineProcess) this;

			pid = inferior.PID;

			operation_completed_event = new ManualResetEvent (false);

			inferior.TargetOutput += new TargetOutputHandler (OnInferiorOutput);
			inferior.DebuggerOutput += new DebuggerOutputHandler (OnDebuggerOutput);
			inferior.DebuggerError += new DebuggerErrorHandler (OnDebuggerError);
		}

		protected TheEngine (SingleSteppingEngine sse, ProcessStart start)
			: this (sse, Inferior.CreateInferior (sse, start))
		{
			inferior.Run (true);
			pid = inferior.PID;

			is_main = true;

			setup_engine ();
		}

		protected TheEngine (SingleSteppingEngine sse, Inferior inferior, int pid)
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
			inferior.SingleSteppingEngine = sse;
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			Report.Debug (DebugFlags.Threads, "New SSE: {0}", this);

			arch = inferior.Architecture;
			disassembler = inferior.Disassembler;

			disassembler.SymbolTable = sse.SymbolTableManager.SimpleSymbolTable;
			current_simple_symtab = sse.SymbolTableManager.SimpleSymbolTable;
			current_symtab = sse.SymbolTableManager.SymbolTable;

			native_language = new Mono.Debugger.Languages.Native.NativeLanguage ((ITargetInfo) inferior);

			sse.SymbolTableManager.SymbolTableChangedEvent +=
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
				send_target_event (result);
				operation_completed_event.Set ();
			}
		}

		void send_target_event (TargetEventArgs args)
		{
			Report.Debug (DebugFlags.EventLoop, "SSE {0} sending target event {1}",
				      this, args);

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
		}

		public override void Start (TargetAddress func, bool is_main)
		{
			if (!func.IsNull) {
				insert_temporary_breakpoint (func);
				current_operation = new Operation (OperationType.Initialize);
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
		//   Checks whether we got a "fatal" event: target died, received a
		//   signal or hit a breakpoint.
		// </summary>
		TargetEventArgs process_child_event (ref Inferior.ChildEvent child_event)
		{
		again:
			Inferior.ChildEventType message = child_event.Type;
			int arg = (int) child_event.Argument;

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
				if (stop_requested) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
					return new TargetEventArgs (TargetEventType.TargetHitBreakpoint, arg, current_frame);
				} else if (step_over_breakpoint (arg, out new_event)) {
					child_event = new_event;
					goto again;
				} else if (!child_breakpoint (arg)) {
					// we hit any breakpoint, but its handler told us
					// to resume the target and continue.
					do_continue ();
					return null;
				}
			}

			if (temp_breakpoint_id != 0) {
				inferior.RemoveBreakpoint (temp_breakpoint_id);
				temp_breakpoint_id = 0;
			}

			switch (message) {
			case Inferior.ChildEventType.CHILD_STOPPED:
				if (stop_requested || (arg != 0)) {
					stop_requested = false;
					frame_changed (inferior.CurrentFrame, null);
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
				frame_changed (inferior.CurrentFrame, null);
				return new TargetEventArgs (TargetEventType.TargetStopped, arg, current_frame);
			}

			return null;
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
				get_backtrace ((int) data);
				break;

			case CommandType.SetRegister:
				set_register ((Register) data);
				break;

			case CommandType.InsertBreakpoint:
				return insert_breakpoint ((BreakpointManager.Handle) data);

			case CommandType.RemoveBreakpoint:
				remove_breakpoint ((int) data);
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
				TargetAddress until = operation.Until;
				if (!until.IsNull)
					do_continue (until);
				else
					do_continue ();
				break;

			case OperationType.StepNativeInstruction:
				do_step ();
				break;

			case OperationType.NextInstruction:
				do_next ();
				break;

			case OperationType.RuntimeInvoke:
				do_runtime_invoke (operation.RuntimeInvokeData);
				break;

			case OperationType.CallMethod:
				do_call_method (operation.CallMethodData);
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
		protected bool ProcessEvent (Inferior.ChildEvent cevent)
		{
			// Callbacks happen when the user (or the engine) called a method
			// in the target (RuntimeInvoke).
			if (cevent.Type == Inferior.ChildEventType.CHILD_CALLBACK) {
				bool ret;
				if (handle_callback (cevent, out ret))
					return ret;
			}

			// If the target stopped abnormally, this returns an event which
			// we should send back to the user to inform him that something
			// went wrong.
			TargetEventArgs result = process_child_event (ref cevent);

		send_result:
			// If `result' is not null, then the target stopped abnormally.
			if (result != null) {
				if (DaemonEventHandler != null) {
					// The `DaemonEventHandler' may decide to discard
					// this event in which case it returns true.
					if (DaemonEventHandler (this, inferior, result))
						return false;
				}
				// Ok, inform the user that we stopped.
				step_operation_finished ();
				operation_completed (result);
				if (is_main && !reached_main) {
					reached_main = true;
					sse.ReachedMain ();
				}
				return true;
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
					if (is_main)
						sse.Initialize (inferior);
				} else if (!DoStep (false))
					return false;
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
				return false;
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

		public string SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (current_simple_symtab == null)
				return null;

			return current_simple_symtab.SimpleLookup (address, exact_match);
		}

		void get_registers ()
		{
			registers = inferior.GetRegisters ();
		}

		void get_backtrace (int max_frames)
		{
			sse.DebuggerBackend.UpdateSymbolTable ();

			Inferior.StackFrame[] iframes = inferior.GetBacktrace (max_frames, main_method_retaddr);
			StackFrame[] frames = new StackFrame [iframes.Length];
			MyBacktrace backtrace = new MyBacktrace (target, arch);

			for (int i = 0; i < iframes.Length; i++) {
				TargetAddress address = iframes [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceAddress source = method.Source.Lookup (address);
					frames [i] = CreateFrame (
						address, i, iframes [i], backtrace, source, method);
				} else
					frames [i] = CreateFrame (
						address, i, iframes [i], backtrace, null, null);
			}

			backtrace.SetFrames (frames);
			current_backtrace = backtrace;
		}

		void set_register (Register reg)
		{
			inferior.SetRegister (reg.Index, (long) reg.Data);
			registers = inferior.GetRegisters ();
		}

		int insert_breakpoint (BreakpointManager.Handle handle)
		{
			return sse.BreakpointManager.InsertBreakpoint (inferior, handle);
		}

		void remove_breakpoint (int index)
		{
			sse.BreakpointManager.RemoveBreakpoint (inferior, index);
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

		protected SingleSteppingEngine sse;
		protected Inferior inferior;
		protected IArchitecture arch;
		protected IDisassembler disassembler;
		ITargetAccess target;
		ISymbolTable current_symtab;
		ISimpleSymbolTable current_simple_symtab;
		ILanguage native_language;
		bool engine_stopped;
		ManualResetEvent operation_completed_event;
		protected bool stop_requested = false;
		bool is_main, reached_main;
		bool native;
		protected int pid, tid;

		protected TargetAddress main_method_retaddr = TargetAddress.Null;
		protected TargetState target_state = TargetState.NO_TARGET;

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
		protected bool StartOperation ()
		{
			lock (this) {
				// First try to get the `command_mutex'.
				// If we succeed, then no synchronous command is currently
				// running.
				if (!sse.AcquireCommandMutex (this)) {
					Report.Debug (DebugFlags.Wait,
						      "SSE {0} cannot get command mutex", this);
					return false;
				}
				// Check whether we're curring performing an async
				// stepping operation.
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "SSE {0} not stopped", this);
					sse.ReleaseCommandMutex ();
					return false;
				}
				// This will never block.  The only thing which can
				// happen here is that we were running an async operation
				// and did not wait for the event yet.
				operation_completed_event.WaitOne ();
				engine_stopped = false;
				Report.Debug (DebugFlags.Wait,
					      "SSE {0} got command mutex", this);
				return true;
			}
		}

		// <summary>
		//   Use this if you previously called StartOperation() and you changed
		//   your mind and don't want to start an operation anymore.
		// </summary>
		protected void AbortOperation ()
		{
			lock (this) {
				Report.Debug (DebugFlags.Wait,
					      "SSE {0} aborted operation", this);
				engine_stopped = true;
				sse.ReleaseCommandMutex ();
			}
		}

		// <summary>
		//   Sends a synchronous command to the engine.
		// </summary>
		protected CommandResult SendSyncCommand (Command command)
		{
			lock (this) {
				// Check whether we're curring performing an async
				// stepping operation.
				if (!engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "SSE {0} not stopped", this);
					return CommandResult.Busy;
				}

				Report.Debug (DebugFlags.Wait,
					      "SSE {0} sending sync command", this);
				CommandResult result = sse.SendSyncCommand (command);
				Report.Debug (DebugFlags.Wait,
					      "SSE {0} finished sync command", this);

				return result;
			}
		}

		protected CommandResult SendSyncCommand (CommandType type, object data1,
							 object data2)
		{
			return SendSyncCommand (new Command (this, type, data1, data2));
		}

		protected CommandResult SendSyncCommand (CommandType type, object data)
		{
			return SendSyncCommand (new Command (this, type, data, null));
		}

		// <summary>
		//   Sends an async command to the engine.
		// </summary>
		// <remarks>
		//   You must call StartOperation() prior to calling this.
		// </remarks>	     
		protected void SendAsyncCommand (Command command, bool wait)
		{
			lock (this) {
				operation_completed_event.Reset ();
				send_target_event (new TargetEventArgs (TargetEventType.TargetRunning, null));
				sse.SendAsyncCommand (command);
			}

			if (wait) {
				Report.Debug (DebugFlags.Wait, "SSE {0} waiting", this);
				operation_completed_event.WaitOne ();
				Report.Debug (DebugFlags.Wait, "SSE {0} done waiting", this);
			}
			Report.Debug (DebugFlags.Wait,
				      "SSE {0} released command mutex", this);
			sse.ReleaseCommandMutex ();
		}

		protected void SendCallbackCommand (Command command)
		{
			if (!StartOperation ())
				throw new TargetNotStoppedException ();

			sse.SendAsyncCommand (command);
		}

		public override bool Stop ()
		{
			return do_wait (true);
		}

		public override bool Wait ()
		{
			return do_wait (false);
		}

		bool do_wait (bool stop)
		{
			lock (this) {
				// First try to get the `command_mutex'.
				// If we succeed, then no synchronous command is currently
				// running.
				if (!sse.AcquireCommandMutex (this)) {
					Report.Debug (DebugFlags.Wait,
						      "SSE {0} cannot get command mutex", this);
					return false;
				}
				// Check whether we're curring performing an async
				// stepping operation.
				if (engine_stopped) {
					Report.Debug (DebugFlags.Wait,
						      "SSE {0} already stopped", this);
					sse.ReleaseCommandMutex ();
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
						sse.ReleaseCommandMutex ();
						return true;
					}
				}
			}

			// Ok, we got the `command_mutex'.
			// Now we can wait for the operation to finish.
			Report.Debug (DebugFlags.Wait, "SSE {0} waiting", this);
			operation_completed_event.WaitOne ();
			Report.Debug (DebugFlags.Wait, "SSE {0} stopped", this);
			sse.ReleaseCommandMutex ();
			return true;
		}

		public void Interrupt ()
		{
			lock (this) {
				Report.Debug (DebugFlags.Wait, "SSE {0} interrupt: {0}",
					      this, engine_stopped);

				if (engine_stopped)
					return;

				stop_requested = true;
				if (!inferior.Stop ()) {
					// We're already stopped, so just consider the
					// current operation as finished.
					step_operation_finished ();
					engine_stopped = true;
					stop_requested = false;
					operation_completed_event.Set ();
				}
			}
		}

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

			BreakpointManager.Handle handle = sse.BreakpointManager.LookupBreakpoint (index);
			if (handle == null)
				return false;

			StackFrame frame = null;
			// Only compute the current stack frame if the handler actually
			// needs it.  Note that this computation is an expensive operation
			// so we should only do it when it's actually needed.
			if (handle.NeedsFrame)
				frame = get_frame (inferior.CurrentFrame);
			if ((handle.CheckHandler != null) &&
			    !handle.CheckHandler (frame, index, handle.UserData))
				return false;

			frame_changed (inferior.CurrentFrame, current_operation);
			send_frame_event (current_frame, handle.BreakpointHandle);

			return true;
		}

		bool step_over_breakpoint (int arg, out Inferior.ChildEvent new_event)
		{
			if (arg == 0) {
				new_event = null;
				return false;
			}

			int index;
			BreakpointManager.Handle handle = sse.BreakpointManager.LookupBreakpoint (
				inferior.CurrentFrame, out index);

			if ((handle != null) && handle.BreakpointHandle.Breaks (this)) {
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
				Operation new_operation = check_method_operation (
					address, current_method, source, operation);
				if (new_operation != null) {
					Report.Debug (DebugFlags.EventLoop,
						      "New operation: {0}", new_operation);
					return new_operation;
				}

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
			temp_breakpoint_id = inferior.InsertBreakpoint (address);
		}

		// <summary>
		//   Single-step one machine instruction.
		// </summary>
		void do_step ()
		{
			check_inferior ();
			frames_invalid ();
			inferior.Step ();
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
			check_inferior ();
			frames_invalid ();
			inferior.Continue ();
		}

		void do_continue (TargetAddress until)
		{
			check_inferior ();
			frames_invalid ();
			insert_temporary_breakpoint (until);
			inferior.Continue ();
		}

		Operation current_operation;

		protected bool Step (Operation operation)
		{
			check_inferior ();

			current_operation = null;
			frames_invalid ();

			Report.Debug (DebugFlags.SSE,
				      "SSE {0} starting step operation {0}",
				      this, operation);

			if (operation.StepFrame == null) {
				do_step ();
				return true;
			}

			current_operation = operation;
			if (DoStep (true)) {
				Report.Debug (DebugFlags.SSE,
					      "SSE {0} finished step operation", this);
				step_operation_finished ();
				return true;
			}
			return false;
		}

		void step_operation_finished ()
		{
			current_operation = null;
		}

		bool do_trampoline (StepFrame frame, TargetAddress trampoline)
		{
			TargetAddress compile = frame.Language.CompileMethodFunc;

			if (compile.IsNull) {
				do_continue (trampoline);
				return false;
			}

			do_callback (new Callback (
				new CallMethodData (compile, trampoline.Address, 0, null),
				new CallbackFunc (callback_method_compiled)));
			return false;
		}

		bool callback_method_compiled (Callback cb, long data1, long data2)
		{
			TargetAddress trampoline = new TargetAddress (sse.AddressDomain, data1);
			IMethod method = null;
			if (current_symtab != null) {
				sse.DebuggerBackend.UpdateSymbolTable ();
				method = Lookup (trampoline);
			}
			if ((method == null) || !method.Module.StepInto) {
				do_next ();
				return false;
			}

			do_continue (trampoline);
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
			StepFrame frame = current_operation.StepFrame;
			if (frame == null)
				return true;

			TargetAddress current_frame = inferior.CurrentFrame;
			bool in_frame = is_in_step_frame (frame, current_frame);
			Report.Debug (DebugFlags.SSE, "SSE {0} stepping at {0} in {1} {2}",
				      current_frame, frame, in_frame);
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
				if (!trampoline.IsNull)
					return do_trampoline (frame, trampoline);

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

		bool handle_callback (Inferior.ChildEvent cevent, out bool ret)
		{
			if ((current_callback == null) ||
			    (cevent.Argument != current_callback.ID)) {
				current_callback = null;
				ret = false;
				return false;
			}

			Callback cb = current_callback;
			current_callback = null;
			ret = cb.Func (cb, cevent.Data1, cevent.Data2);
			return true;
		}

		void do_callback (Callback cb)
		{
			if (current_callback != null)
				throw new InternalError ();

			current_callback = cb;
			cb.Call (inferior);
		}

		Callback current_callback;

		protected delegate bool CallbackFunc (Callback cb, long data1, long data2);

		protected sealed class Callback
		{
			public readonly long ID;
			public readonly CallMethodData Data;
			public readonly CallbackFunc Func;

			static int next_id = 0;

			public Callback (CallMethodData data, CallbackFunc func)
			{
				this.ID = ++next_id;
				this.Data = data;
				this.Func = func;
			}

			public void Call (Inferior inferior)
			{
				switch (Data.Type) {
				case CallMethodType.LongLong:
					inferior.CallMethod (Data.Method, Data.Argument1,
							     Data.Argument2, ID);
					break;

				case CallMethodType.LongString:
					inferior.CallMethod (Data.Method, Data.Argument1,
							     Data.StringArgument, ID);
					break;

				case CallMethodType.RuntimeInvoke:
					RuntimeInvokeData rdata = (RuntimeInvokeData) Data.Data;
					inferior.RuntimeInvoke (
						rdata.Language.RuntimeInvokeFunc,
						rdata.MethodArgument, rdata.ObjectArgument,
						rdata.ParamObjects, ID);
					break;

				default:
					throw new InvalidOperationException ();
				}
			}
		}

		void do_runtime_invoke (RuntimeInvokeData rdata)
		{
			check_inferior ();
			frames_invalid ();

			do_callback (new Callback (
				new CallMethodData (rdata.Language.CompileMethodFunc,
						    rdata.MethodArgument.Address, 0, rdata),
				new CallbackFunc (callback_do_runtime_invoke)));
		}

		bool callback_do_runtime_invoke (Callback cb, long data1, long data2)
		{
			TargetAddress invoke = new TargetAddress (sse.AddressDomain, data1);

			Report.Debug (DebugFlags.EventLoop, "Runtime invoke: {0}", invoke);

			// insert_temporary_breakpoint (invoke);

			do_callback (new Callback (
				new CallMethodData ((RuntimeInvokeData) cb.Data.Data),
				new CallbackFunc (callback_runtime_invoke_done)));

			return true;
		}

		bool callback_runtime_invoke_done (Callback cb, long data1, long data2)
		{
			RuntimeInvokeData rdata = (RuntimeInvokeData) cb.Data.Data;

			Report.Debug (DebugFlags.EventLoop,
				      "Runtime invoke done: {0:x} {1:x}",
				      data1, data2);

			rdata.InvokeOk = true;
			rdata.ReturnObject = new TargetAddress (inferior.AddressDomain, data1);
			if (data2 != 0)
				rdata.ExceptionObject = new TargetAddress (inferior.AddressDomain, data2);
			else
				rdata.ExceptionObject = TargetAddress.Null;

			frame_changed (inferior.CurrentFrame, null);
			send_frame_event (current_frame, 0);
			return true;
		}

		void do_call_method (CallMethodData cdata)
		{
			do_callback (new Callback (
				cdata, new CallbackFunc (callback_call_method)));
		}

		bool callback_call_method (Callback cb, long data1, long data2)
		{
			cb.Data.Result = data1;

			// sse.SetCompleted (this);
			return true;
		}

		bool stopped;
		Inferior.ChildEvent stop_event;

		// <summary>
		//   Interrupt any currently running stepping operation, but don't send
		//   any notifications to the caller.  The currently running operation is
		//   automatically resumed when ReleaseThreadLock() is called.
		// </summary>
		public override Register[] AcquireThreadLock ()
		{
			stopped = inferior.Stop (out stop_event);

			get_registers ();
			return registers;
		}

		public override void ReleaseThreadLock ()
		{
			// If the target was already stopped, there's nothing to do for us.
			if (!stopped)
				return;
			else if (stop_event != null) {
				// The target stopped before we were able to send the SIGSTOP,
				// but we haven't processed this event yet.
				Inferior.ChildEvent cevent = stop_event;
				stop_event = null;

				if (sse.HandleChildEvent (inferior, cevent))
					return;
				ProcessEvent (cevent);
			} else {
				do_continue ();
			}
		}

		//
		// Backtrace.
		//

		protected class MyBacktrace : Backtrace
		{
			public MyBacktrace (ITargetAccess target, IArchitecture arch)
				: base (target, arch, null)
			{
			}

			public void SetFrames (StackFrame[] frames)
			{
				this.frames = frames;
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

	//
	// Calling methods.
	//

	protected enum CallMethodType
	{
		LongLong,
		LongString,
		RuntimeInvoke
	}

	protected sealed class CallMethodData
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

		public CallMethodData (RuntimeInvokeData rdata)
		{
			this.Type = CallMethodType.RuntimeInvoke;
			this.Data = rdata;
		}
	}

	protected sealed class RuntimeInvokeData
	{
		public readonly ILanguageBackend Language;
		public readonly TargetAddress MethodArgument;
		public readonly TargetAddress ObjectArgument;
		public readonly TargetAddress[] ParamObjects;

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
				lock (this) {
					if (is_daemon)
						return TargetState.DAEMON;
					else
						return target_state;
				}
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

			SendSyncCommand (CommandType.GetBacktrace, -1);

			return current_backtrace;
		}

		public override Backtrace GetBacktrace (int max_frames)
		{
			check_inferior ();

			if ((max_frames == -1) && (current_backtrace != null))
				return current_backtrace;

			SendSyncCommand (CommandType.GetBacktrace, max_frames);

			return current_backtrace;
		}

		public override Register GetRegister (int index)
		{
			foreach (Register register in GetRegisters ()) {
				if (register.Index == index)
					return register;
			}

			throw new NoSuchRegisterException ();
		}

		public override Register[] GetRegisters ()
		{
			check_inferior ();

			return registers;
		}

		public override void SetRegister (int register, long value)
		{
			Register reg = new Register (register, value);
			SendSyncCommand (CommandType.SetRegister, reg);
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

		bool start_step_operation (Operation operation, bool wait)
		{
			check_inferior ();
			if (!StartOperation ())
				return false;
			SendAsyncCommand (new Command (this, operation), wait);
			return true;
		}

		bool start_step_operation (OperationType operation, TargetAddress until,
					   bool wait)
		{
			return start_step_operation (new Operation (operation, until), wait);
		}

		bool start_step_operation (OperationType operation, bool wait)
		{
			return start_step_operation (new Operation (operation), wait);
		}

		void call_method (CallMethodData cdata)
		{
			SendCallbackCommand (new Command (this, new Operation (cdata)));
		}

		void call_method (RuntimeInvokeData rdata)
		{
			SendCallbackCommand (new Command (this, new Operation (rdata)));
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public override bool StepInstruction (bool wait)
		{
			return start_step_operation (OperationType.StepInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public override bool StepNativeInstruction (bool wait)
		{
			return start_step_operation (OperationType.StepNativeInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public override bool NextInstruction (bool wait)
		{
			return start_step_operation (OperationType.NextInstruction, wait);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public override bool StepLine (bool wait)
		{
			return start_step_operation (OperationType.StepLine, wait);
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public override bool NextLine (bool wait)
		{
			return start_step_operation (OperationType.NextLine, wait);
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public override bool Finish (bool wait)
		{
			check_inferior ();
			if (!StartOperation ())
				return false;

			StackFrame frame = CurrentFrame;
			if (frame.Method == null) {
				AbortOperation ();
				throw new NoMethodException ();
			}

			StepFrame sf = new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress,
				null, StepMode.Finish);

			Operation operation = new Operation (OperationType.StepFrame, sf);
			SendAsyncCommand (new Command (this, operation), wait);
			return true;
		}

		public override bool Continue (TargetAddress until, bool in_background, bool wait)
		{
			if (in_background)
				return start_step_operation (OperationType.RunInBackground,
							     until, wait);
			else
				return start_step_operation (OperationType.Run, until, wait);
		}

		public override void Kill ()
		{
			Dispose ();
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

			CommandResult result = SendSyncCommand (
				CommandType.InsertBreakpoint, data);
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
				SendSyncCommand (CommandType.RemoveBreakpoint, index);
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

		public int GetInstructionSize (TargetAddress address)
		{
			check_inferior ();
			CommandResult result = SendSyncCommand (CommandType.GetInstructionSize, address);
			if (result.Type == CommandResultType.CommandOk) {
				return (int) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			check_inferior ();
			CommandResult result = SendSyncCommand (CommandType.DisassembleInstruction, method, address);
			if (result.Type == CommandResultType.CommandOk) {
				return (AssemblerLine) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				return null;
		}

		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			check_inferior ();
			CommandResult result = SendSyncCommand (CommandType.DisassembleMethod, method);
			if (result.Type == CommandResultType.CommandOk)
				return (AssemblerMethod) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public override long CallMethod (TargetAddress method, long method_argument,
						 string string_argument)
		{
			CallMethodData data = new CallMethodData (
				method, method_argument, string_argument, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();
			return (long) data.Result;
		}

		public override TargetAddress CallMethod (TargetAddress method, string arg)
		{
			CallMethodData data = new CallMethodData (method, 0, arg, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();
			long retval = (long) data.Result;
			if (inferior.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (inferior.AddressDomain, retval);
		}

		public override TargetAddress CallMethod (TargetAddress method,
							  TargetAddress arg1,
							  TargetAddress arg2)
		{
			CallMethodData data = new CallMethodData (
				method, arg1.Address, arg2.Address, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();

			long retval = (long) data.Result;
			if (inferior.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (inferior.AddressDomain, retval);
		}

		protected bool RuntimeInvoke (ILanguageBackend language,
					      TargetAddress method_argument,
					      TargetAddress object_argument,
					      TargetAddress[] param_objects)
		{
			RuntimeInvokeData data = new RuntimeInvokeData (
				language, method_argument, object_argument, param_objects);
			return start_step_operation (new Operation (data), true);
		}

		protected TargetAddress RuntimeInvoke (ILanguageBackend language,
						       TargetAddress method_argument,
						       TargetAddress object_argument,
						       TargetAddress[] param_objects,
						       out TargetAddress exc_object)
		{
			RuntimeInvokeData data = new RuntimeInvokeData (
				language, method_argument, object_argument, param_objects);

			call_method (data);
			if (!data.InvokeOk)
				throw new Exception ();

			exc_object = data.ExceptionObject;
			return data.ReturnObject;
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

		protected byte[] read_memory (TargetAddress address, int size)
		{
			CommandResult result = SendSyncCommand (CommandType.ReadMemory, address, size);
			if (result.Type == CommandResultType.CommandOk)
				return (byte []) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		string read_string (TargetAddress address)
		{
			CommandResult result = SendSyncCommand (CommandType.ReadString, address);
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

		protected void write_memory (TargetAddress address, byte[] buffer)
		{
			CommandResult result = SendSyncCommand (CommandType.WriteMemory, address, buffer);
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

		void ITargetAccess.WriteBuffer (TargetAddress address, byte[] buffer)
		{
			write_memory (address, buffer);
		}

		void ITargetAccess.WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteAddress (TargetAddress address, TargetAddress value)
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
			ILanguageBackend lbackend;

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
				this.lbackend = method.Module.LanguageBackend as ILanguageBackend;
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

			public override Process Process {
				get { return sse; }
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

			public override bool RuntimeInvoke (TargetAddress method_argument,
							    TargetAddress object_argument,
							    TargetAddress[] param_objects)
			{
				if (lbackend == null)
					throw new InvalidOperationException ();

				return sse.RuntimeInvoke (lbackend, method_argument,
							  object_argument, param_objects);
			}

			public override TargetAddress RuntimeInvoke (TargetAddress method_arg,
								     TargetAddress object_arg,
								     TargetAddress[] param,
								     out TargetAddress exc_obj)
			{
				if (lbackend == null)
					throw new InvalidOperationException ();

				return sse.RuntimeInvoke (lbackend, method_arg,
							  object_arg, param, out exc_obj);
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
				mono_debugger_server_abort_wait ();
			}
			inferior_thread.Join ();
		}

		TheEngine[] threads = new TheEngine [thread_hash.Count];
		thread_hash.Values.CopyTo (threads, 0);
		for (int i = 0; i < threads.Length; i++)
			threads [i].Dispose ();
	}
}
}
