using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using ST = System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Backend;
using Mono.Debugger.Architectures;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal class ProcessServant : DebuggerMarshalByRefObject, IOperationHost
	{
		Process client;
		TargetInfo target_info;
		ThreadManager manager;
		Architecture architecture;
		OperatingSystemBackend os;
		NativeLanguage native_language;
		SymbolTableManager symtab_manager;
		MonoThreadManager mono_manager;
		BreakpointManager breakpoint_manager;
		Dictionary<int,ExceptionCatchPoint> exception_handlers;
		ProcessStart start;
		DebuggerSession session;
		protected MonoLanguageBackend mono_language;
		protected ThreadServant main_thread;
		Hashtable thread_hash;

		ProcessServant parent;

		ThreadDB thread_db;

		bool is_attached;
		bool is_execed;
		bool is_forked;
		bool initialized;
		DebuggerMutex thread_lock_mutex;
		bool has_thread_lock;

		int id = ++next_id;
		static int next_id = 0;

		private ProcessServant (ThreadManager manager, DebuggerSession session)
		{
			this.manager = manager;
			this.session = session;
			this.client = manager.Debugger.Client.CreateProcess (this);

			thread_lock_mutex = new DebuggerMutex ("thread_lock_mutex");

			stopped_event = new ST.ManualResetEvent (false);

			thread_hash = Hashtable.Synchronized (new Hashtable ());

			target_info = Inferior.GetTargetInfo ();
			if (target_info.TargetAddressSize == 8)
				architecture = new Architecture_X86_64 (this, target_info);
			else
				architecture = new Architecture_I386 (this, target_info);
		}

		internal ProcessServant (ThreadManager manager, ProcessStart start)
			: this (manager, start.Session)
		{
			this.start = start;

			is_attached = start.PID != 0;

			breakpoint_manager = new BreakpointManager ();

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();

			symtab_manager = new SymbolTableManager (session);

			os = Inferior.CreateOperatingSystemBackend (this);
			native_language = new NativeLanguage (this, os, target_info);

			session.OnProcessCreated (client);
		}

		private ProcessServant (ProcessServant parent, int pid)
			: this (parent.manager, parent.session)
		{
			this.start = new ProcessStart (parent.ProcessStart, pid);

			this.is_forked = true;
			this.initialized = true;

			this.parent = parent;

			breakpoint_manager = new BreakpointManager (parent.breakpoint_manager);

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();
			foreach (KeyValuePair<int,ExceptionCatchPoint> catchpoint in parent.exception_handlers)
				exception_handlers.Add (catchpoint.Key, catchpoint.Value);

			symtab_manager = parent.symtab_manager;

			native_language = parent.native_language;
			os = parent.os;
		}

		public int ID {
			get { return id; }
		}

		public bool IsAttached {
			get { return is_attached; }
		}

		public bool IsExeced {
			get { return is_execed; }
		}

		public Process Client {
			get { return client; }
		}

		public DebuggerSession Session {
			get { return session; }
		}

		internal ThreadManager ThreadManager {
			get { return manager; }
		}

		internal Architecture Architecture {
			get { return architecture; }
		}

		internal BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		internal SymbolTableManager SymbolTableManager {
			get {
				return symtab_manager;
			}
		}

		public NativeLanguage NativeLanguage {
			get { return native_language; }
		}

		public OperatingSystemBackend OperatingSystem {
			get { return os; }
		}

		public ThreadServant MainThread {
			get { return main_thread; }
		}

		public DebuggerServant Debugger {
			get { return manager.Debugger; }
		}

		internal ProcessStart ProcessStart {
			get { return start; }
		}

		internal MonoThreadManager MonoManager {
			get { return mono_manager; }
		}

		internal bool MonoRuntimeFound {
			get; private set;
		}

		public bool IsManaged {
			get { return mono_manager != null; }
		}

		internal bool CanExecuteCode {
			get { return (mono_manager != null) && mono_manager.CanExecuteCode; }
		}

		public string TargetApplication {
			get { return start.TargetApplication; }
		}

		public string[] CommandLineArguments {
			get { return start.CommandLineArguments; }
		}

		internal ST.WaitHandle WaitHandle {
			get { return stopped_event; }
		}

		internal void ThreadCreated (Inferior inferior, int pid, bool do_attach, bool resume_thread)
		{
			Inferior new_inferior = inferior.CreateThread (pid, do_attach);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (manager, this, new_inferior, pid);

			Report.Debug (DebugFlags.Threads, "Thread created: {0} {1} {2}", pid, new_thread, do_attach);

			if (mono_manager != null)
				mono_manager.ThreadCreated (new_thread);

			if (!do_attach && !is_execed)
				get_thread_info (inferior, new_thread);
			OnThreadCreatedEvent (new_thread);

			if (resume_thread) {
				CommandResult result = current_operation != null ?
					current_operation : new ThreadCommandResult (new_thread.Thread);
				new_thread.StartThread (result);
			}
		}

		internal void ChildForked (Inferior inferior, int pid)
		{
			ProcessServant new_process = new ProcessServant (this, pid);

			Inferior new_inferior = Inferior.CreateInferior (
				manager, new_process, new_process.ProcessStart);
			new_inferior.InitializeThread (pid);

			if (!manager.Debugger.Configuration.FollowFork) {
				new_inferior.DetachAfterFork ();
				return;
			}

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, new_process, new_inferior, pid);

			Report.Debug (DebugFlags.Threads, "Child forked: {0} {1}", pid, new_thread);

			new_process.main_thread = new_thread;

			manager.Debugger.OnProcessCreatedEvent (new_process);
			new_process.OnThreadCreatedEvent (new_thread);

			CommandResult result = new_process.CloneParentOperation (new_thread);
			new_thread.StartForkedChild (result);
		}

		internal void ChildExecd (Inferior inferior)
		{
			is_execed = true;

			if (!is_forked) {
				if (mono_language != null)
					mono_language.Dispose();

				if (native_language != null)
					native_language.Dispose ();

				if (os != null)
					os.Dispose ();

				if (symtab_manager != null)
					symtab_manager.Dispose ();
			}

			if (breakpoint_manager != null)
				breakpoint_manager.Dispose ();

			session = session.Clone (client, start.Options, "@" + id);
			session.OnProcessCreated (client);

			breakpoint_manager = new BreakpointManager ();

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();

			symtab_manager = new SymbolTableManager (session);

			os = Inferior.CreateOperatingSystemBackend (this);
			native_language = new NativeLanguage (this, os, target_info);

			Inferior new_inferior = Inferior.CreateInferior (manager, this, start);
			new_inferior.InitializeAfterExec (inferior.PID);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, this, new_inferior, inferior.PID);

			ThreadServant[] threads;
			lock (thread_hash.SyncRoot) {
				threads = new ThreadServant [thread_hash.Count];
				thread_hash.Values.CopyTo (threads, 0);
			}

			for (int i = 0; i < threads.Length; i++) {
				if (threads [i].PID != inferior.PID)
					threads [i].Kill ();
			}

			thread_hash [inferior.PID] = new_thread;

			inferior.Dispose ();
			inferior = null;

			manager.Debugger.OnProcessExecdEvent (this);
			manager.Debugger.OnThreadCreatedEvent (new_thread.Thread);
			initialized = is_forked = false;
			main_thread = new_thread;

			CommandResult result = CloneParentOperation (new_thread);
			new_thread.StartExecedChild (result);
		}

		internal CommandResult StartApplication ()
		{
			SingleSteppingEngine engine = new SingleSteppingEngine (manager, this, start);

			initialized = true;

			this.main_thread = engine;
			engine.Thread.ThreadFlags |= Thread.Flags.StopOnExit;

			if (thread_hash.Contains (engine.PID))
				thread_hash [engine.PID] = engine;
			else
				thread_hash.Add (engine.PID, engine);
			session.MainThreadGroup.AddThread (engine.Thread.ID);

			session.OnMainProcessCreated (client);
			manager.Debugger.OnMainProcessCreatedEvent (this);

			CommandResult result = Debugger.StartOperation (start.Session.Config.ThreadingModel, engine);
			return engine.StartApplication (result);
		}

		internal void OnProcessExitedEvent ()
		{
			DropGlobalThreadLock ();

			if (current_state == ProcessState.Running) {
				current_state = ProcessState.Exited;
				current_operation.Completed ();
				current_operation = null;
				stopped_event.Set ();
			}

			if (!is_forked)
				session.OnProcessExited (client);
			session.MainThreadGroup.RemoveThread (main_thread.ID);
			manager.Debugger.OnProcessExitedEvent (this);
		}

		protected void OnThreadCreatedEvent (ThreadServant thread)
		{
			thread_hash.Add (thread.PID, thread);
			manager.Debugger.OnThreadCreatedEvent (thread.Client);
		}

		internal void OnManagedThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
		}

		internal void OnProcessReachedMainEvent ()
		{
			manager.Debugger.Client.OnProcessReachedMainEvent (client);
		}

		internal void OnThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
			thread.ThreadGroup.RemoveThread (thread.ID);
			session.DeleteThreadGroup (thread.ThreadGroup.Name);
			manager.Debugger.OnThreadExitedEvent (thread.Client);

			if (thread_hash.Count == 0)
				OnProcessExitedEvent ();
		}

		internal void OnTargetOutput (bool is_stderr, string output)
		{
			client.OnTargetOutput (is_stderr, output);
		}

		internal void InitializeMono (Inferior inferior, TargetAddress mdb_debug_info)
		{
			MonoRuntimeFound = true;
			mono_manager = MonoThreadManager.Initialize (this, inferior, mdb_debug_info, is_attached);

			InitializeThreads (inferior, !is_attached);

			if (mono_manager == null)
				return;

			mono_manager.InitializeThreads (inferior);

			if (is_attached)
				mono_manager.InitializeAfterAttach (inferior);
		}

		internal void InitializeThreads (Inferior inferior, bool resume_threads)
		{
			if (thread_db != null)
				return;

			thread_db = ThreadDB.Create (this, inferior);
			if (thread_db == null) {
				if (IsManaged)
					Report.Error ("Failed to initialize thread_db on {0}",
						      start.CommandLine);
				return;
			}

			int[] threads = inferior.GetThreads ();
			foreach (int thread in threads) {
				if (thread_hash.Contains (thread))
					continue;
				ThreadCreated (inferior, thread, true, resume_threads);
			}

			thread_db.GetThreadInfo (inferior, delegate (int lwp, long tid) {
				SingleSteppingEngine engine = (SingleSteppingEngine) thread_hash [lwp];
				if (engine == null) {
					Report.Error ("Unknown thread {0} in {1}", lwp,
						      start.CommandLine);
					return;
				}
				engine.SetTID (tid);
			});
		}

		public bool CheckForThreads(ArrayList check_threads)
		{
			if(thread_db == null)
				return false;
			thread_db.GetThreadInfo (null, delegate (int lwp, long tid) {
				check_threads.Add(lwp);
			} );
			return true;
		}

		void get_thread_info (Inferior inferior, SingleSteppingEngine engine)
		{
			if (thread_db == null) {
				if (mono_manager == null)
					return;
				Report.Error ("Failed to initialize thread_db on {0}: {1} {2}",
					      start.CommandLine, start, Environment.StackTrace);
				throw new InternalError ();
			}

			bool found = false;
			thread_db.GetThreadInfo (inferior, delegate (int lwp, long tid) {
				if (lwp != engine.PID)
					return;

				engine.SetTID (tid);
				found = true;
			});

			if (!found)
				Report.Error ("Cannot find thread {0:x} in {1}",
					      engine.PID, start.CommandLine);
		}

		internal SingleSteppingEngine GetEngineByTID (Inferior inferior, long tid)
		{
			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine.TID == tid)
					return engine;
			}

			if (thread_db == null) {
				Report.Error ("Failed to initialize thread_db on {0}: {1} {2}",
					      start.CommandLine, start, Environment.StackTrace);
				throw new InternalError ();
			}

			SingleSteppingEngine result = null;
			thread_db.GetThreadInfo (inferior, delegate (int t_lwp, long t_tid) {
				if (tid != t_tid)
					return;
				result = (SingleSteppingEngine) thread_hash [t_lwp];

			});

			if (result == null)
				Report.Error ("Cannot find thread {0:x} in {1}",
					      tid, start.CommandLine);

			return result;
		}

		public void Kill ()
		{
			if (!Inferior.HasThreadEvents) {
				SingleSteppingEngine[] sses = new SingleSteppingEngine [thread_hash.Count];
				thread_hash.Values.CopyTo (sses, 0);
				foreach (SingleSteppingEngine sse in sses)
					sse.SetKilledFlag ();
				foreach (SingleSteppingEngine sse in sses)
					sse.Kill ();
			} else {
				main_thread.Kill ();
			}
		}

		public void Detach ()
		{
			if (!IsAttached)
				throw new TargetException (TargetError.CannotDetach);

			main_thread.Detach ();
		}

		internal void OnTargetDetached ()
		{
			OnProcessExitedEvent ();
			Dispose ();
		}

		internal MonoLanguageBackend CreateMonoLanguage (MonoDebuggerInfo info)
		{
			mono_language = new MonoLanguageBackend (this, info);
			return mono_language;
		}

		internal void UpdateSymbolTable (TargetMemoryAccess target)
		{
			if (mono_language != null)
				mono_language.Update (target);
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			if (mono_language == null)
				throw new SymbolTableException (
						"Cannot load .NET assembly `{0}' while " +
						"debugging an unmanaged application.",
						filename);

			if (!mono_language.TryFindImage (thread, filename))
				throw new SymbolTableException ("Could not find any .NET assembly `{0}'.", filename);
		}

		internal MonoLanguageBackend MonoLanguage {
			get {
				if (mono_language == null)
					throw new InvalidOperationException ();

				return mono_language;
			}
		}

		internal bool IsManagedApplication {
			get { return mono_language != null; }
		}

		public Module[] Modules {
			get { return session.Modules; }
		}

		public SourceLocation FindLocation (string file, int line)
		{
			foreach (Module module in Modules) {
				SourceLocation location = module.FindLocation (file, line);
				
				if (location != null)
					return location;
			}

			return null;
		}

		public SourceLocation FindMethod (string name)
		{
			foreach (Module module in Modules) {
				MethodSource method = module.FindMethod (name);
				
				if (method != null)
					return new SourceLocation (method);
			}

			return null;
		}

		internal ThreadServant[] ThreadServants {
			get {
				lock (thread_hash.SyncRoot) {
					int count = thread_hash.Count;
					ThreadServant[] servants = new ThreadServant [count];
					thread_hash.Values.CopyTo (servants, 0);
					return servants;
				}
			}
		}

		internal SingleSteppingEngine[] Engines {
			get {
				lock (thread_hash.SyncRoot) {
					int count = thread_hash.Count;
					SingleSteppingEngine[] engines = new SingleSteppingEngine [count];
					thread_hash.Values.CopyTo (engines, 0);
					return engines;
				}
			}
		}

		public TargetAddress LookupSymbol (string name)
		{
			return os.LookupSymbol (name);
		}

		public Thread[] GetThreads ()
		{
			lock (thread_hash.SyncRoot) {
				int count = thread_hash.Count;
				Thread[] threads = new Thread [count];
				ThreadServant[] servants = new ThreadServant [count];
				thread_hash.Values.CopyTo (servants, 0);
				for (int i = 0; i < count; i++)
					threads [i] = servants [i].Client;
				return threads;
			}
		}

		internal bool HasThreadLock {
			get { return has_thread_lock; }
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		internal void AcquireGlobalThreadLock (SingleSteppingEngine caller)
		{
			if (has_thread_lock)
				throw new InternalError ("Recursive thread lock");

			thread_lock_mutex.Lock ();
			Report.Debug (DebugFlags.Threads,
				      "Acquiring global thread lock: {0}", caller);
			has_thread_lock = true;
			foreach (ThreadServant thread in thread_hash.Values) {
				if (thread == caller)
					continue;
				thread.AcquireThreadLock ();
			}
			Report.Debug (DebugFlags.Threads,
				      "Done acquiring global thread lock: {0}",
				      caller);
		}

		internal void ReleaseGlobalThreadLock (SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Releasing global thread lock: {0}", caller);
				
			foreach (ThreadServant thread in thread_hash.Values) {
				if (thread == caller)
					continue;
				thread.ReleaseThreadLock ();
			}
			has_thread_lock = false;
			thread_lock_mutex.Unlock ();
			Report.Debug (DebugFlags.Threads,
				      "Released global thread lock: {0}", caller);
		}

		internal void DropGlobalThreadLock ()
		{
			if (thread_hash.Count != 0)
				throw new InternalError ();

			if (has_thread_lock) {
				has_thread_lock = false;
				thread_lock_mutex.Unlock ();
			}
		}

#region User Threads

		internal enum ProcessState
		{
			SingleThreaded,
			Running,
			Stopping,
			Stopped,
			Exited
		}

		protected ST.ManualResetEvent stopped_event;

		ProcessState current_state = ProcessState.Stopped;
		OperationCommandResult current_operation = null;

		public void OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if (current_state == ProcessState.Stopping)
				return;
			else if (current_state != ProcessState.Running)
				throw new InternalError ();

			if ((result != null) && (caller != main_thread) &&
			    ((result.Type == TargetEventType.TargetExited) || (result.Type == TargetEventType.TargetSignaled)))
				return;

			current_state = ProcessState.Stopping;
			SuspendUserThreads (model, caller);

			lock (this) {
				current_state = ProcessState.Stopped;
				current_operation.Completed ();
				current_operation = null;
				stopped_event.Set ();
			}
		}

		protected void SuspendUserThreads (ThreadingModel model, SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Suspending user threads: {0} {1}", model, caller);

			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				Report.Debug (DebugFlags.Threads, "  check user thread: {0} {1}",
					      engine, engine.Thread.ThreadFlags);
				if (engine == caller)
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Immutable) != 0) &&
				    ((model & ThreadingModel.StopImmutableThreads) == 0))
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Daemon) != 0) &&
				    ((model & ThreadingModel.StopDaemonThreads) == 0))
					continue;
				engine.SuspendUserThread ();
			}

			Report.Debug (DebugFlags.Threads,
				      "Done suspending user threads: {0} {1}", model, caller);
		}

		protected void ResumeUserThreads (ThreadingModel model, SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Resuming user threads: {0}", caller);

			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine == caller)
					continue;
				if ((engine.Thread.ThreadFlags & Thread.Flags.AutoRun) == 0)
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Immutable) != 0) &&
				    ((model & ThreadingModel.StopImmutableThreads) == 0))
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Daemon) != 0) &&
				    ((model & ThreadingModel.StopDaemonThreads) == 0))
					continue;

				CommandResult result;
				if (current_operation != null)
					result = current_operation;
				else
					result = new ThreadCommandResult (engine.Thread);

				engine.ResumeUserThread (result);
			}

			Report.Debug (DebugFlags.Threads,
				      "Resumed user threads: {0}", caller);
		}

		internal CommandResult StartOperation (ThreadingModel model, SingleSteppingEngine caller)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if ((current_state != ProcessState.Stopped) && (current_state != ProcessState.SingleThreaded))
				throw new TargetException (TargetError.NotStopped);

			if ((model & ThreadingModel.ThreadingMode) == ThreadingModel.Single) {
				current_state = ProcessState.SingleThreaded;
				if ((model & ThreadingModel.ResumeThreads) != 0)
					ResumeUserThreads (model, caller);
				return new ThreadCommandResult (caller.Thread);
			} else if ((model & ThreadingModel.ThreadingMode) != ThreadingModel.Process) {
				throw new ArgumentException ();
			}

			lock (this) {
				current_state = ProcessState.Running;
				stopped_event.Reset ();
				current_operation = new OperationCommandResult (this, model);
			}

			ResumeUserThreads (model, caller);
			return current_operation;
		}

		internal void StartGlobalOperation (ThreadingModel model, SingleSteppingEngine caller, OperationCommandResult operation)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if ((current_state != ProcessState.Stopped) && (current_state != ProcessState.SingleThreaded))
				throw new TargetException (TargetError.NotStopped);

			lock (this) {
				current_state = ProcessState.Running;
				stopped_event.Reset ();
				current_operation = operation;
			}

			ResumeUserThreads (model, caller);
		}

		protected CommandResult CloneParentOperation (SingleSteppingEngine new_thread)
		{
			if (parent.current_state == ProcessState.SingleThreaded) {
				current_state = ProcessState.SingleThreaded;
				return new ThreadCommandResult (new_thread.Thread);
			}

			if (parent.current_state != ProcessState.Running)
				throw new InternalError ();

			current_state = ProcessState.Running;
			if ((parent.current_operation.ThreadingModel & ThreadingModel.ThreadingMode) == ThreadingModel.Global)
				current_operation = parent.current_operation;
			else if ((parent.current_operation.ThreadingModel & ThreadingModel.ThreadingMode) == ThreadingModel.Process)
				current_operation = new OperationCommandResult (this, parent.current_operation.ThreadingModel);
			else
				throw new InternalError ();

			return current_operation;
		}

		internal void Stop ()
		{
			main_thread.Invoke (delegate {
				current_state = ProcessState.Stopping;

				SuspendUserThreads (ThreadingModel.Process, null);
				current_state = ProcessState.Stopped;
				if (current_operation != null) {
					current_operation.Completed ();
					current_operation = null;
				}
				stopped_event.Set ();
				return null;
			}, null);
		}

		ST.WaitHandle IOperationHost.WaitHandle {
			get { return stopped_event; }
		}

		void IOperationHost.SendResult (SingleSteppingEngine sse, TargetEventArgs args)
		{
			Debugger.SendTargetEvent (sse, args);
		}

		void IOperationHost.Abort ()
		{
			Stop ();
		}

#endregion

		public bool ActivatePendingBreakpoints (CommandResult result)
		{
			return ((SingleSteppingEngine) main_thread).ManagedCallback (
				delegate (SingleSteppingEngine sse) {
					return sse.ActivatePendingBreakpoints (null);
				}, result);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void DoDispose ()
		{
			if (!is_forked) {
				if (architecture != null) {
					architecture.Dispose ();
					architecture = null;
				}

				if (mono_language != null) {
					mono_language.Dispose();
					mono_language = null;
				}

				if (native_language != null) {
					native_language.Dispose ();
					native_language = null;
				}

				if (os != null) {
					os.Dispose ();
					os = null;
				}

				if (symtab_manager != null) {
					symtab_manager.Dispose ();
					symtab_manager = null;
				}
			}

			if (breakpoint_manager != null) {
				breakpoint_manager.Dispose ();
				breakpoint_manager = null;
			}

			if (thread_db != null) {
				thread_db.Dispose ();
				thread_db = null;
			}

			if (thread_lock_mutex != null) {
				thread_lock_mutex.Dispose ();
				thread_lock_mutex = null;
			}

			exception_handlers = null;

			manager.RemoveProcess (this);
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ProcessServant ()
		{
			Dispose (false);
		}
	}
}
