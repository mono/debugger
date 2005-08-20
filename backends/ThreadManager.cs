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
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public delegate void ThreadEventHandler (ThreadManager manager, Process process);

	public class ThreadManager : MarshalByRefObject
	{
		internal ThreadManager (DebuggerBackend backend)
		{
			this.backend = backend;

			breakpoint_manager = new BreakpointManager ();

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			
			thread_lock_mutex = new DebuggerMutex ("thread_lock_mutex");
			address_domain = new AddressDomain ("global");

			start_event = new ManualResetEvent (false);
			completed_event = new AutoResetEvent (false);
			command_mutex = new DebuggerMutex ("command_mutex");
			command_mutex.DebugFlags = DebugFlags.Wait;

			ready_event = new ManualResetEvent (false);
			engine_event = Semaphore.CreateThreadManagerSemaphore ();
			wait_event = new AutoResetEvent (false);

			mono_debugger_server_global_init ();
		}

		public static void Initialize ()
		{
			mono_debugger_server_static_init ();
		}

		SingleSteppingEngine the_engine;

		ProcessStart start;
		DebuggerBackend backend;
		BreakpointManager breakpoint_manager;
		Thread inferior_thread;
		Thread wait_thread;
		ManualResetEvent ready_event;
		AutoResetEvent wait_event;
		Semaphore engine_event;
		Hashtable thread_hash;

		bool has_thread_lock;
		DebuggerMutex thread_lock_mutex;
		AddressDomain address_domain;

		Process main_process;

		ManualResetEvent start_event;
		AutoResetEvent completed_event;
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
			start_event.WaitOne ();

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
			ready_event.WaitOne ();

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

		public bool HasTarget {
			get { return inferior_thread != null; }
		}

		int next_process_id = 0;
		internal int NextProcessID {
			get { return ++next_process_id; }
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
			engine.Process.Kill ();
			OnThreadExitedEvent (engine.Process);
		}

		void Kill ()
		{
			foreach (SingleSteppingEngine thread in thread_hash.Values) {
				thread.Kill ();
			}

			the_engine.Kill ();
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
			    (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED)) {
				abort_requested = true;
				Kill ();
				OnTargetExitedEvent ();
			}

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

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		public event ThreadEventHandler InitializedEvent;
		public event ThreadEventHandler MainThreadCreatedEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event TargetExitedHandler TargetExitedEvent;

		void OnInitializedEvent (Process new_process)
		{
			if (InitializedEvent != null)
				InitializedEvent (this, new_process);
		}

		void OnMainThreadCreatedEvent (Process new_process)
		{
			if (MainThreadCreatedEvent != null)
				MainThreadCreatedEvent (this, new_process);
		}

		void OnThreadCreatedEvent (Process new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		void OnThreadExitedEvent (Process process)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, process);
		}

		void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
			backend.Dispose ();
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

		internal IMessage SendSyncCommand (IMethodCallMessage message, IMessageSink sink)
		{
			Command command = new Command (CommandType.Message, message, sink);

			if (!AcquireCommandMutex (null)) {
				TargetException ex = new TargetException (TargetError.NotStopped);
				return new ReturnMessage (ex, message);
			}

			lock (this) {
				current_command = command;
				sync_command_running = true;
				engine_event.Set ();
			}

			return null;
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

			if (abort_requested) {
				Report.Debug (DebugFlags.Wait, "Engine thread abort requested");
				Kill ();
				return;
			}

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
			if (command.Type == CommandType.Message) {
				IMessage return_message;
				try {
					return_message = ChannelServices.SyncDispatchMessage (
						(IMessage) command.Data1);
				} catch (ThreadAbortException) {
					return;
				} catch (Exception ex) {
					return_message = new ReturnMessage (
						ex, (IMethodCallMessage) command.Data1);
				}

				((IMessageSink) command.Data2).SyncProcessMessage (return_message);

				lock (this) {
					current_command = null;
					sync_command_running = false;
					completed_event.Set ();
					ReleaseCommandMutex ();
				}
			} else if (command.Type == CommandType.Operation) {
				try {
					command.Engine.ProcessOperation (command.Operation);
				} catch (ThreadAbortException) {
					return;
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0} {1}", command, e);
				}
			} else {
				throw new InternalError ();
			}
		}

		void start_wait_thread ()
		{
			Report.Debug (DebugFlags.Threads, "Wait thread started: {0}",
				      DebuggerWaitHandle.CurrentThread);

			while (true) {
				Report.Debug (DebugFlags.Wait, "Wait thread sleeping");
				wait_event.WaitOne ();

				int pid, status;
				if (abort_requested) {
					Report.Debug (DebugFlags.Wait,
						      "Wait thread abort requested");

					//
					// Reap all our children.
					//

					do {
						pid = mono_debugger_server_global_wait (out status);
						Report.Debug (DebugFlags.Wait,
							      "Wait thread received event: {0} {1:x}",
							      pid, status);
					} while (pid > 0);

					Report.Debug (DebugFlags.Wait,
						      "Wait thread exiting");

					return;
				}

				Report.Debug (DebugFlags.Wait, "Wait thread waiting");

				//
				// Wait until we got an event from the target or a command from the user.
				//

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
			}
		}

#region IDisposable implementation
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

				abort_requested = true;
				wait_event.Set();
				engine_event.Set();
				disposed = true;
			}

			//
			// There are two situations where Dispose() can be called:
			//
			// a) It's a user-requested `kill' or `quit'.
			//
			//    In this case, the wait thread is normally blocking in waitpid()
			//    (via mono_debugger_server_global_wait ()).
			//
			//    To wake it up, the engine thread must issue a
			//    ptrace (PTRACE_KILL, inferior->pid) - note that the same restriction
			//    apply like for any other ptrace() call, so this can only be done
			//    from the engine thread.
			//
			//    To do that, we just set the `abort_requested' flag here and then
			//    join the engine thread - after it exited, we also join the wait
			//    thread so it can reap the dead child.
			//
			//    Once both threads exited, we can go ahead and dispose everything.
			//
			// b) The child exited.
			//
			//    In this case, we're invoked from the engine thread via the
			//    `ThreadExitEvent' (that's why we must not join the engine thread).
			//
			//    The child is already dead, so we just set the flag and join the
			//    wait thread [note that the wait thread is already dying at this point;
			//    it was blocking on the `wait_event', woke up and found the
			//    `abort_requested' - so we only join it to avoid a race condition].
			//
			//    After that, we can go ahead and dispose everything.
			//

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (inferior_thread == null)
					return;

				if (Thread.CurrentThread != inferior_thread)
					inferior_thread.Join ();
				wait_thread.Join ();

				bool main_in_threads = false;

				SingleSteppingEngine[] threads = new SingleSteppingEngine [thread_hash.Count];
				thread_hash.Values.CopyTo (threads, 0);

				for (int i = 0; i < threads.Length; i++) {
					if (main_process == threads[i].Process)
						main_in_threads = true;
					threads [i].Dispose ();
				}

				if (main_process != null && !main_in_threads)
					main_process.Dispose ();

				if (breakpoint_manager != null)
					breakpoint_manager.Dispose ();

				if (the_engine != null)
					the_engine.Dispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion

		~ThreadManager ()
		{
			Dispose (false);
		}
	}
}
