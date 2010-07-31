using System;
using System.IO;
using System.Linq;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Languages;

namespace Mono.Debugger.Backend
{
	internal class ThreadManager : DebuggerMarshalByRefObject
	{
		public static TimeSpan WaitTimeout = TimeSpan.FromMilliseconds (5000);

		internal ThreadManager (Debugger debugger)
		{
			this.debugger = debugger;

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			engine_hash = Hashtable.Synchronized (new Hashtable ());
			processes = ArrayList.Synchronized (new ArrayList ());

			pending_events = Hashtable.Synchronized (new Hashtable ());

			last_pending_sigstop = DateTime.Now;
			pending_sigstops = new Dictionary<int,DateTime> ();
			
			address_domain = AddressDomain.Global;

			wait_event = new ST.AutoResetEvent (false);
			engine_event = new ST.ManualResetEvent (true);
			ready_event = new ST.ManualResetEvent (false);

			event_queue = new DebuggerEventQueue ("event_queue");
			event_queue.DebugFlags = DebugFlags.Wait;

			mono_debugger_server_global_init ();

			wait_thread = new ST.Thread (new ST.ThreadStart (start_wait_thread));
			wait_thread.IsBackground = true;
			wait_thread.Start ();

			inferior_thread = new ST.Thread (new ST.ThreadStart (start_inferior));
			inferior_thread.IsBackground = true;
			inferior_thread.Start ();

			ready_event.WaitOne ();
		}

		Debugger debugger;
		DebuggerEventQueue event_queue;
		ST.Thread inferior_thread;
		ST.Thread wait_thread;
		ST.ManualResetEvent ready_event;
		ST.ManualResetEvent engine_event;
		ST.AutoResetEvent wait_event;
		Hashtable thread_hash;
		Hashtable engine_hash;
		Hashtable pending_events;
		ArrayList processes;

		AddressDomain address_domain;

		DateTime last_pending_sigstop;
		Dictionary<int,DateTime> pending_sigstops;

		bool abort_requested;
		bool waiting;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_wait (out int status);

		[DllImport("monodebuggerserver")]
		static extern Inferior.ChildEventType mono_debugger_server_dispatch_simple (int status, out int arg);

		void start_inferior ()
		{
			event_queue.Lock ();
			ready_event.Set ();

			while (!abort_requested) {
				engine_thread_main ();
			}

			Report.Debug (DebugFlags.Threads, "Engine thread exiting.");
		}

		// <remarks>
		//   These three variables are shared between the two threads, so you need to
		//   lock (this) before accessing/modifying them.
		// </remarks>
		Command current_command = null;
		SingleSteppingEngine current_event = null;
		int current_event_status = 0;

		public ProcessServant OpenCoreFile (ProcessStart start, out Thread[] threads)
		{
			CoreFile core = CoreFile.OpenCoreFile (this, start);
			threads = core.GetThreads ();
			return core;
		}

		internal void AddEngine (SingleSteppingEngine engine)
		{
			thread_hash.Add (engine.PID, engine);
			engine_hash.Add (engine.ID, engine);
		}

		internal void RemoveProcess (ProcessServant process)
		{
			processes.Remove (process);
		}

		internal SingleSteppingEngine GetEngine (int id)
		{
			return (SingleSteppingEngine) engine_hash [id];
		}

		public bool HasTarget {
			get { return inferior_thread != null; }
		}

		static int next_process_id = 0;
		internal int NextThreadID {
			get { return ++next_process_id; }
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref Inferior.ChildEvent cevent, out bool resume_target)
		{
			if (cevent.Type == Inferior.ChildEventType.NONE) {
				resume_target = true;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_CREATED_THREAD) {
				int pid = (int) cevent.Argument;
				inferior.Process.ThreadCreated (inferior, pid, false, true);
				if (pending_sigstops.ContainsKey (pid))
					pending_sigstops.Remove (pid);
				resume_target = true;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_FORKED) {
				inferior.Process.ChildForked (inferior, (int) cevent.Argument);
				resume_target = true;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_EXECD) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				inferior.Process.ChildExecd (engine, inferior);
				resume_target = false;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) {
				if (cevent.Argument == inferior.SIGCHLD) {
					cevent = new Inferior.ChildEvent (
						Inferior.ChildEventType.CHILD_STOPPED, 0, 0, 0);
					resume_target = true;
					return true;
				} else if (inferior.Has_SIGWINCH && (cevent.Argument == inferior.SIGWINCH)) {
					resume_target = true;
					return true;
				} else if (inferior.HasSignals && (cevent.Argument == inferior.Kernel_SIGRTMIN+1)) {
					// __SIGRTMIN and __SIGRTMIN+1 are used internally by the threading library
					resume_target = true;
					return true;
				}
			}

			if (inferior.Process.OperatingSystem.CheckForPendingMonoInit (inferior)) {
				resume_target = true;
				return true;
			}

			bool retval = false;
			resume_target = false;
			if (inferior.Process.MonoManager != null)
				retval = inferior.Process.MonoManager.HandleChildEvent (
					engine, inferior, ref cevent, out resume_target);

			if ((cevent.Type == Inferior.ChildEventType.CHILD_EXITED) ||
			    (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED)) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				engine.OnThreadExited (cevent);
				resume_target = false;
				return true;
			}

			return retval;
		}

		internal bool HasPendingSigstopForNewThread (int pid)
		{
			if (!pending_sigstops.ContainsKey (pid))
				return false;

			pending_sigstops.Remove (pid);
			return true;
		}

		public Debugger Debugger {
			get { return debugger; }
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		internal bool InBackgroundThread {
			get { return ST.Thread.CurrentThread == inferior_thread; }
		}

		internal object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target,
					     object user_data)
		{
			Command command = new Command (sse, target, user_data);

			if (!engine_event.WaitOne (WaitTimeout, false))
				throw new TargetException (TargetError.NotStopped);

			event_queue.Lock ();
			engine_event.Reset ();

			current_command = command;

			event_queue.Signal ();
			event_queue.Unlock ();

			engine_event.WaitOne ();

			if (command.Result is Exception)
				throw (Exception) command.Result;
			else
				return command.Result;
		}

		public ProcessServant StartApplication (ProcessStart start, out CommandResult result)
		{
			Command command = new Command (CommandType.CreateProcess, start);

			if (!engine_event.WaitOne (WaitTimeout, false))
				throw new TargetException (TargetError.NotStopped);

			event_queue.Lock ();
			engine_event.Reset ();

			current_command = command;

			event_queue.Signal ();
			event_queue.Unlock ();

			engine_event.WaitOne ();

			if (command.Result is Exception)
				throw (Exception) command.Result;
			else {
				var pair = (KeyValuePair<CommandResult,ProcessServant>) command.Result;
				result = pair.Key;
				return pair.Value;
			}
		}

		internal void AddPendingEvent (SingleSteppingEngine engine, Inferior.ChildEvent cevent)
		{
			Report.Debug (DebugFlags.Wait, "Add pending event: {0} {1}", engine, cevent);
			pending_events.Add (engine, cevent);
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

			event_queue.Wait ();

			Report.Debug (DebugFlags.Wait, "ThreadManager done waiting");

			if (abort_requested) {
				Report.Debug (DebugFlags.Wait, "Engine thread abort requested");
				return;
			}

			int status;
			SingleSteppingEngine event_engine;
			Command command;

			Report.Debug (DebugFlags.Wait, "ThreadManager woke up: {0} {1:x} {2}",
				      current_event, current_event_status, current_command);

			event_engine = current_event;
			status = current_event_status;

			current_event = null;
			current_event_status = 0;

			command = current_command;
			current_command = null;

			if (event_engine != null) {
				try {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process event: {1}",
						      DebuggerWaitHandle.CurrentThread, event_engine);
					event_engine.ProcessEvent (status);
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process event done: {1}",
						      DebuggerWaitHandle.CurrentThread, event_engine);
				} catch (ST.ThreadAbortException) {
					;
				} catch (Exception e) {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager caught exception: {0}", e);
					Console.WriteLine ("EXCEPTION: {0}", e);
				}

				check_pending_events ();

				if (command == null)
					engine_event.Set ();
				RequestWait ();
			}

			if (command == null)
				return;

			// These are synchronous commands; ie. the caller blocks on us
			// until we finished the command and sent the result.
			if (command.Type == CommandType.TargetAccess) {
				try {
					if(command.Engine.Inferior != null)
						command.Result = command.Engine.Invoke (
							(TargetAccessDelegate) command.Data1, command.Data2);
				} catch (ST.ThreadAbortException) {
					return;
				} catch (Exception ex) {
					command.Result = ex;
				}

				check_pending_events ();

				engine_event.Set ();
			} else if (command.Type == CommandType.CreateProcess) {
				try {
					ProcessStart start = (ProcessStart) command.Data1;
					ProcessServant process = new ProcessServant (this, start);
					processes.Add (process);

					CommandResult result = process.StartApplication ();

					RequestWait ();

					command.Result = new KeyValuePair<CommandResult,ProcessServant> (result, process);
				} catch (ST.ThreadAbortException) {
					return;
				} catch (Exception ex) {
					command.Result = ex;
				}

				engine_event.Set ();
			} else {
				throw new InvalidOperationException ();
			}
		}

		void check_pending_events ()
		{
			SingleSteppingEngine[] list = new SingleSteppingEngine [pending_events.Count];
			pending_events.Keys.CopyTo (list, 0);

			for (int i = 0; i < list.Length; i++) {
				SingleSteppingEngine engine = list [i];
				if (engine.Process.HasThreadLock)
					continue;

				Inferior.ChildEvent cevent = (Inferior.ChildEvent) pending_events [engine];
				pending_events.Remove (engine);

				try {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process pending event: {1} {2}",
						      DebuggerWaitHandle.CurrentThread, engine, cevent);
					engine.ReleaseThreadLock (cevent);
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process pending event done: {1}",
						      DebuggerWaitHandle.CurrentThread, engine);
				} catch (ST.ThreadAbortException) {
					;
				} catch (Exception e) {
					Report.Debug (DebugFlags.Wait,
					      "ThreadManager caught exception: {0}", e);
				}
			}
		}

		void start_wait_thread ()
		{
			Report.Debug (DebugFlags.Threads, "Wait thread started: {0}",
				      DebuggerWaitHandle.CurrentThread);

			//
			// NOTE: Dispose() intentionally uses
			//          wait_thread.Abort ();
			//          wait_thread.Join ();
			//
			// The Thread.Abort() is neccessary since we may be blocked in a
			// waitpid().  In this case, the thread abort signal which is sent
			// to the current thread will make the waitpid() abort with an EINTR,
			// so we're not deadlocking here.
			//

			try {
				while (wait_thread_main ())
					;
			} catch (ST.ThreadAbortException) {
				Report.Debug (DebugFlags.Threads, "Wait thread abort: {0}",
					      DebuggerWaitHandle.CurrentThread);
				ST.Thread.ResetAbort ();
			} catch (Exception ex) {
				Report.Debug (DebugFlags.Threads, "FUCK: {0}", ex);
				throw;
			}

			Report.Debug (DebugFlags.Threads, "Wait thread exiting: {0}",
				      DebuggerWaitHandle.CurrentThread);
		}

		bool wait_thread_main ()
		{
			Report.Debug (DebugFlags.Wait, "Wait thread sleeping");
			wait_event.WaitOne ();
			waiting = true;

		again:
			Report.Debug (DebugFlags.Wait, "Wait thread again");

			int pid = 0, status = 0;
			if (abort_requested) {
				Report.Debug (DebugFlags.Wait,
					      "Wait thread abort requested");

				//
				// Reap all our children.
				//

				do {
					Report.Debug (DebugFlags.Wait,
						      "Wait thread reaping children");
					pid = mono_debugger_server_global_wait (out status);
					Report.Debug (DebugFlags.Wait,
						      "Wait thread received event: {0} {1:x}",
						      pid, status);
				} while (pid > 0);

				Report.Debug (DebugFlags.Wait,
					      "Wait thread done");

				return false;
			}

			if (DateTime.Now - last_pending_sigstop > new TimeSpan (0, 2, 30)) {
				foreach (int pending in pending_sigstops.Keys) {
					Report.Error ("Got SIGSTOP from unknown PID {0}!", pending);
				}

				pending_sigstops.Clear ();
				last_pending_sigstop = DateTime.Now;
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

			if (abort_requested || (pid <= 0))
				return true;

			if(!Inferior.HasThreadEvents)
			{
				int arg;
				Inferior.ChildEventType etype = mono_debugger_server_dispatch_simple (status, out arg);
				SingleSteppingEngine engine = (SingleSteppingEngine) thread_hash [pid];
				if(etype == Inferior.ChildEventType.CHILD_EXITED) {
					if(engine != null) {
						SingleSteppingEngine[] sses = new SingleSteppingEngine [thread_hash.Count];
						thread_hash.Values.CopyTo (sses, 0);
						foreach(SingleSteppingEngine sse in sses)
							sse.ProcessEvent (status);
						Dispose();
						waiting = false;
						return true;
					}
					else
						goto again;
				}

				if (engine == null) {
					SingleSteppingEngine[] sses = new SingleSteppingEngine [thread_hash.Count];
					thread_hash.Values.CopyTo (sses, 0);				
					Inferior inferior = sses[0].Inferior;		
					inferior.Process.ThreadCreated (inferior, pid, false, true);
					goto again;
				}

				ArrayList check_threads = new ArrayList();
				bool got_threads = true;
				foreach(ProcessServant process in processes)
					got_threads = got_threads && process.CheckForThreads(check_threads);

				if(got_threads) {
					int[] lwps = new int [thread_hash.Count];
					thread_hash.Keys.CopyTo (lwps, 0);
					foreach(int lwp in lwps) {
						if(!check_threads.Contains(lwp)) {
							SingleSteppingEngine old_engine = (SingleSteppingEngine) thread_hash [lwp];					
							thread_hash.Remove (old_engine.PID);
							engine_hash.Remove (old_engine.ID);
							old_engine.ProcessServant.OnThreadExitedEvent (old_engine);
							old_engine.Dispose ();
						}
					}
				}
			}

			SingleSteppingEngine event_engine = (SingleSteppingEngine) thread_hash [pid];
			if (event_engine == null && Inferior.HasThreadEvents) {
				int arg;
				Inferior.ChildEventType etype = mono_debugger_server_dispatch_simple (status, out arg);

				/*
				 * Ignore exit events from unknown children.
				 */

				if ((etype == Inferior.ChildEventType.CHILD_EXITED) && (arg == 0))
					goto again;

				/*
				 * There is a race condition in the Linux kernel which shows up on >= 2.6.27:
				 *
				 * When creating a new thread, the initial stopping event of that thread is sometimes
				 * sent before sending the `PTRACE_EVENT_CLONE' for it.
				 *
				 * Because of this, we explicitly wait for the new thread to stop and ignore any
				 * "early" stopping signals.
				 *
				 * See also the comments in _server_ptrace_wait_for_new_thread() in x86-linux-ptrace.c
				 * and bugs #423518 and #466012.
				 *
				 */

				if ((etype != Inferior.ChildEventType.CHILD_STOPPED) || (arg != 0)) {
					Report.Error ("WARNING: Got event {0:x} for unknown pid {1}", status, pid);
					waiting = false;
					RequestWait ();
					return true;
				}

				if (!pending_sigstops.ContainsKey (pid))
					pending_sigstops.Add (pid, DateTime.Now);

				Report.Debug (DebugFlags.Wait, "Ignoring SIGSTOP from unknown pid {0}.", pid);
				goto again;
			}

			engine_event.WaitOne ();

			event_queue.Lock ();
			engine_event.Reset ();

			if (current_event != null) {
				Console.WriteLine ("Current_event is not null: {0}", Environment.StackTrace);
				throw new InternalError ();
			}

			current_event = event_engine;
			current_event_status = status;

			waiting = false;

			event_queue.Signal ();
			event_queue.Unlock ();
			return true;
		}

		private void RequestWait ()
		{
			if (waiting)
				throw new InternalError ();
			Report.Debug (DebugFlags.Wait, "Signalling wait event");
			wait_event.Set ();
		}

		protected struct PendingEventInfo
		{
			public readonly int PID;
			public readonly int Status;
			public readonly DateTime TimeStamp;

			public PendingEventInfo (int pid, int status)
			{
				this.PID = pid;
				this.Status = status;
				this.TimeStamp = DateTime.Now;
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
#if FIXME
				RequestWait ();
#endif
				if (event_queue.TryLock ()) {
					event_queue.Signal();
					event_queue.Unlock ();
				}
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

				Report.Debug (DebugFlags.Wait,
					      "Thread manager dispose");

				if (ST.Thread.CurrentThread != inferior_thread)
					inferior_thread.Join ();
				wait_thread.Abort ();
				wait_thread.Join ();

				ProcessServant[] procs = new ProcessServant [processes.Count];
				processes.CopyTo (procs, 0);

				for (int i = 0; i < procs.Length; i++)
					procs [i].Dispose ();
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
