using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public delegate void ThreadEventHandler (ThreadManager manager, Process process);

	public class ThreadManager
	{
		internal ThreadManager (DebuggerBackend backend)
		{
			this.backend = backend;
			this.SymbolTableManager = backend.SymbolTableManager;

			breakpoint_manager = new BreakpointManager ();

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			
			thread_lock_mutex = new DebuggerMutex ("thread_lock_mutex");
			address_domain = new AddressDomain ("global");

			start_event = new DebuggerManualResetEvent ("start_event", false);
			completed_event = new DebuggerAutoResetEvent ("completed_event", false);
			command_mutex = new DebuggerMutex ("command_mutex");
			command_mutex.DebugFlags = DebugFlags.SSE;

			ready_event = new DebuggerManualResetEvent ("ready_event", false);
			engine_event = Semaphore.CreateThreadManagerSemaphore ();
			wait_event = new DebuggerAutoResetEvent ("wait_event", false);

			mono_debugger_server_global_init ();
		}

		public static void Initialize ()
		{
			mono_debugger_server_static_init ();
		}

		SingleSteppingEngine the_engine;
		internal readonly SymbolTableManager SymbolTableManager;

		ProcessStart start;
		DebuggerBackend backend;
		BreakpointManager breakpoint_manager;
		Thread inferior_thread;
		Thread wait_thread;
		DebuggerManualResetEvent ready_event;
		DebuggerEvent wait_event;
		Semaphore engine_event;
		Hashtable thread_hash;

		bool has_thread_lock;
		DebuggerMutex thread_lock_mutex;
		AddressDomain address_domain;

		Process main_process;

		DebuggerEvent start_event;
		DebuggerEvent completed_event;
		DebuggerMutex command_mutex;
		bool sync_command_running;
		bool abort_requested;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_static_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_wait (out int status);

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_get_pending_sigint ();

		void start_inferior ()
		{
			try {
				the_engine = new SingleSteppingEngine (this, start);
			} catch (Exception ex) {
				engine_error (ex);
				return;
			}

			Report.Debug (DebugFlags.Threads, "Thread manager ({0}) started: {1}",
				      DebuggerWaitHandle.CurrentThread, the_engine.PID);

			thread_hash.Add (the_engine.PID, the_engine);

			OnThreadCreatedEvent (the_engine.Process);

			wait_event.Set ();

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
		SingleSteppingEngine command_engine = null;
		SingleSteppingEngine current_event = null;
		int current_event_status = 0;

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
			start_event.Wait ();

			if (start_error != null)
				throw start_error;
		}

		public void StartApplication (ProcessStart start)
		{
			this.start = start;

			wait_thread = new Thread (new ThreadStart (start_wait_thread));
			wait_thread.IsBackground = true;
			wait_thread.Start ();

			inferior_thread = new Thread (new ThreadStart (start_inferior));
			inferior_thread.IsBackground = true;
			inferior_thread.Start ();

			wait_until_engine_is_ready ();
		}

		public Process WaitForApplication ()
		{
			ready_event.Wait ();

			return main_process;
		}

		bool initialized;
		MonoThreadManager mono_manager;
		TargetAddress main_method = TargetAddress.Null;

		internal void Initialize (Inferior inferior)
		{
			if (inferior.CurrentFrame != main_method)
				throw new InternalError ("Target stopped unexpectedly at {0}, " +
							 "but main is at {1}", inferior.CurrentFrame, main_method);

			backend.ReachedMain ();
			inferior.UpdateModules ();
		}

		internal void ReachedMain ()
		{
			OnInitializedEvent (main_process);
			OnMainThreadCreatedEvent (main_process);

			ready_event.Set ();
		}

		public Process[] Threads {
			get {
				lock (this) {
					Process[] procs = new Process [thread_hash.Count];
					int i = 0;
					foreach (SingleSteppingEngine engine in thread_hash.Values)
						procs [i] = engine.Process;
					return procs;
				}
			}
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		internal void AcquireGlobalThreadLock (SingleSteppingEngine caller)
		{
			thread_lock_mutex.Lock ();
			Report.Debug (DebugFlags.Threads,
				      "Acquiring global thread lock: {0}", caller);
			has_thread_lock = true;
			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine == caller)
					continue;
				if (engine.AcquireThreadLock ())
					wait_event.Set ();
			}
			Report.Debug (DebugFlags.Threads,
				      "Done acquiring global thread lock: {0}",
				      caller);
		}

		internal void ReleaseGlobalThreadLock (SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Releasing global thread lock: {0}", caller);
				
			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine == caller)
					continue;
				engine.ReleaseThreadLock ();
			}
			has_thread_lock = false;
			thread_lock_mutex.Unlock ();
			Report.Debug (DebugFlags.Threads,
				      "Released global thread lock: {0}", caller);
		}

		void thread_created (Inferior inferior, int pid)
		{
			Report.Debug (DebugFlags.Threads, "Thread created: {0}", pid);

			Inferior new_inferior = inferior.CreateThread ();

			SingleSteppingEngine new_thread = new SingleSteppingEngine (this, new_inferior, pid);

			thread_hash.Add (pid, new_thread);

			if ((mono_manager != null) &&
			    mono_manager.ThreadCreated (new_thread, new_inferior, inferior)) {
				main_process = new_thread.Process;

				main_method = mono_manager.Initialize (the_engine, inferior);

				Report.Debug (DebugFlags.Threads,
					      "Managed main address is {0}",
					      main_method);

				new_thread.Start (main_method, true);
			}

			new_inferior.Continue ();
			OnThreadCreatedEvent (new_thread.Process);

			inferior.Continue ();
		}

		internal void KillThread (SingleSteppingEngine engine)
		{
			thread_hash.Remove (engine.PID);
			OnThreadExitedEvent (engine.Process);
			engine.Process.Kill ();
		}

		internal bool HandleChildEvent (Inferior inferior,
						ref Inferior.ChildEvent cevent)
		{
			if (cevent.Type == Inferior.ChildEventType.NONE) {
				inferior.Continue ();
				return true;
			}

			if (!initialized) {
				if ((cevent.Type != Inferior.ChildEventType.CHILD_STOPPED) ||
				    (cevent.Argument != 0))
					throw new InternalError (
						"Received unexpected initial child event {0}",
						cevent);

				mono_manager = MonoThreadManager.Initialize (this, inferior);

				main_process = the_engine.Process;
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

			bool retval = false;
			if (mono_manager != null)
				retval = mono_manager.HandleChildEvent (inferior, ref cevent);

			if ((cevent.Type == Inferior.ChildEventType.CHILD_EXITED) ||
			    (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED))
				OnTargetExitedEvent ();

			return retval;
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		internal BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		public Process MainProcess {
			get { return main_process; }
		}

		public event ThreadEventHandler InitializedEvent;
		public event ThreadEventHandler MainThreadCreatedEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event TargetExitedHandler TargetExitedEvent;

		public event TargetOutputHandler TargetOutputEvent;
		public event TargetOutputHandler TargetErrorOutputEvent;
		public event DebuggerOutputHandler DebuggerOutputEvent;
		public event DebuggerErrorHandler DebuggerErrorEvent;

		protected virtual void OnInitializedEvent (Process new_process)
		{
			if (InitializedEvent != null)
				InitializedEvent (this, new_process);
		}

		protected virtual void OnMainThreadCreatedEvent (Process new_process)
		{
			if (MainThreadCreatedEvent != null)
				MainThreadCreatedEvent (this, new_process);
		}

		protected virtual void OnThreadCreatedEvent (Process new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		protected virtual void OnThreadExitedEvent (Process process)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, process);
		}

		protected virtual void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		public void Kill ()
		{
			if (main_process != null)
				main_process.Kill ();
		}

		void inferior_output (bool is_stderr, string line)
		{
			if (TargetOutputEvent != null)
				TargetOutputEvent (is_stderr, line);
		}

		void debugger_output (string line)
		{
			if (DebuggerOutputEvent != null)
				DebuggerOutputEvent (line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			if (DebuggerErrorEvent != null)
				DebuggerErrorEvent (this, message, e);
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
		internal bool AcquireCommandMutex (SingleSteppingEngine engine)
		{
			if (!command_mutex.TryLock ())
				return false;

			command_engine = engine;
			return true;
		}

		internal void ReleaseCommandMutex ()
		{
			command_engine = null;
			command_mutex.Unlock ();
		}

		internal bool InBackgroundThread {
			get { return Thread.CurrentThread == inferior_thread; }
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
		internal CommandResult SendSyncCommand (Command command)
		{
			if (InBackgroundThread) {
				try {
					return command.Engine.ProcessCommand (command);
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
				// completed_event.Reset ();
				sync_command_running = true;
				engine_event.Set ();
			}

			completed_event.Wait ();

			CommandResult result;
			lock (this) {
				result = command_result;
				command_result = null;
				current_command = null;
			}

			ReleaseCommandMutex ();
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
		internal void SendAsyncCommand (Command command)
		{
			lock (this) {
				current_command = command;
				engine_event.Set ();
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
			Report.Debug (DebugFlags.Wait, "ThreadManager waiting");

			engine_event.Wait ();

			Report.Debug (DebugFlags.Wait, "ThreadManager woke up");

			int status;
			SingleSteppingEngine event_engine;

			lock (this) {
				event_engine = current_event;
				status = current_event_status;

				current_event = null;
				current_event_status = 0;
			}

			if (event_engine != null) {
				try {
					event_engine.ProcessEvent (status);
				} catch (ThreadAbortException) {
					;
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0}", e);
				}

				lock (this) {
					wait_event.Set ();
				}

				if (!engine_is_ready) {
					engine_is_ready = true;
					start_event.Set ();
				}
				return;
			}

			//
			// We caught a SIGINT.
			//
			if (mono_debugger_server_get_pending_sigint () > 0) {
				Report.Debug (DebugFlags.EventLoop,
					      "ThreadManager received SIGINT: {0} {1}",
					      command_engine, sync_command_running);

				lock (this) {
					if (sync_command_running) {
						command_result = CommandResult.Interrupted;
						current_command = null;
						sync_command_running = false;
						completed_event.Set ();
						return;
					}

					foreach (SingleSteppingEngine engine in thread_hash.Values) {
						if (!engine.IsDaemon)
							engine.Interrupt ();
					}

					if (has_thread_lock) {
						Report.Debug (DebugFlags.Threads,
							      "Aborting global thread lock");

						has_thread_lock = false;
						thread_lock_mutex.Unlock ();
					}
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

			Report.Debug (DebugFlags.EventLoop,
				      "ThreadManager received command: {0}", command);

			// These are synchronous commands; ie. the caller blocks on us
			// until we finished the command and sent the result.
			if (command.Type != CommandType.Operation) {
				CommandResult result;
				try {
					result = command.Engine.ProcessCommand (command);
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
					command.Engine.ProcessCommand (command.Operation);
				} catch (ThreadAbortException) {
					return;
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0} {1}", command, e);
				}
			}
		}

		void start_wait_thread ()
		{
			while (!abort_requested) {
				wait_thread_main ();
			}
		}

		void wait_thread_main ()
		{
			Report.Debug (DebugFlags.Wait, "Wait thread sleeping");
			wait_event.Wait ();

		again:
			Report.Debug (DebugFlags.Wait, "Wait thread waiting");

			//
			// Wait until we got an event from the target or a command from the user.
			//

			int pid, status;
			pid = mono_debugger_server_global_wait (out status);

			Report.Debug (DebugFlags.Wait,
				      "Wait thread received event: {0} {1:x}",
				      pid, status);

			//
			// Note: `pid' is basically just an unique number which identifies the
			//       SingleSteppingEngine of this event.
			//

			if (pid > 0) {
				SingleSteppingEngine event_engine = (SingleSteppingEngine) thread_hash [pid];
				if (event_engine == null)
					throw new InternalError ("Got event {0:x} for unknown pid {1}",
								 status, pid);

				lock (this) {
					if (current_event != null)
						throw new InternalError ();

					current_event = event_engine;
					current_event_status = status;
					engine_event.Set ();
				}
			}

			if (abort_requested) {
				Report.Debug (DebugFlags.Wait, "Abort requested");
				return;
			}
		}

		//
		// IDisposable
		//

		protected virtual void DoDispose ()
		{
			if (inferior_thread != null) {
				if (inferior_thread != Thread.CurrentThread)
					inferior_thread.Abort ();
			}
			if (wait_thread != null)
				wait_thread.Abort ();

			SingleSteppingEngine[] threads = new SingleSteppingEngine [thread_hash.Count];
			thread_hash.Values.CopyTo (threads, 0);

			for (int i = 0; i < threads.Length; i++)
				threads [i].Dispose ();

			if (main_process != null)
				main_process.Dispose ();
		}

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				DoDispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ThreadManager ()
		{
			Dispose (false);
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}
	}
}
