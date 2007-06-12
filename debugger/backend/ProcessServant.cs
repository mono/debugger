using System;
using System.IO;
using System.Collections;
using ST = System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backends
{
	internal class ProcessServant : DebuggerMarshalByRefObject
	{
		Process client;
		ThreadManager manager;
		BfdContainer bfd_container;
		SymbolTableManager symtab_manager;
		MonoLanguageBackend mono_language;
		MonoThreadManager mono_manager;
		BreakpointManager breakpoint_manager;
		ProcessStart start;
		DebuggerSession session;
		protected ThreadServant main_thread;
		ArrayList languages;
		Hashtable thread_hash;

		bool is_attached;
		bool is_forked;
		bool initialized;
		ST.ManualResetEvent initialized_event;
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

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			initialized_event = new ST.ManualResetEvent (false);
		}

		internal ProcessServant (ThreadManager manager, ProcessStart start)
			: this (manager, start.Session)
		{
			this.start = start;

			is_attached = start.PID != 0;

			breakpoint_manager = new BreakpointManager ();

			symtab_manager = new SymbolTableManager (session);

			languages = new ArrayList ();
			bfd_container = new BfdContainer (this);

			session.OnProcessCreated (client);
		}

		private ProcessServant (ProcessServant parent, int pid)
			: this (parent.manager, parent.session)
		{
			this.start = new ProcessStart (parent.ProcessStart, pid);

			this.is_forked = true;
			this.initialized = true;

			breakpoint_manager = new BreakpointManager (parent.breakpoint_manager);

			symtab_manager = parent.symtab_manager;

			languages = parent.languages;
			bfd_container = parent.bfd_container;
		}

		public int ID {
			get { return id; }
		}

		public bool IsAttached {
			get { return is_attached; }
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

		internal BfdContainer BfdContainer {
			get {
				return bfd_container;
			}
		}

		internal BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		internal SymbolTableManager SymbolTableManager {
			get {
				return symtab_manager;
			}
		}

		public Language NativeLanguage {
			get {
				return bfd_container.NativeLanguage;
			}
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

		internal void AddLanguage (ILanguageBackend language)
		{
			languages.Add (language);
		}

		internal ArrayList Languages {
			get { return languages; }
		}

		internal MonoThreadManager MonoManager {
			get { return mono_manager; }
		}

		public bool IsManaged {
			get { return mono_manager != null; }
		}

		public string TargetApplication {
			get { return start.TargetApplication; }
		}

		public string[] CommandLineArguments {
			get { return start.CommandLineArguments; }
		}

		internal void ThreadCreated (Inferior inferior, int pid, bool do_attach)
		{
			Inferior new_inferior = inferior.CreateThread ();

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, this, new_inferior, pid);

			Report.Debug (DebugFlags.Threads, "Thread created: {0} {1}", pid, new_thread);

			// Order is important: first add the new engine to the manager's hash table,
			//                     then call inferior.Initialize() / inferior.Attach().
			manager.AddEngine (new_thread);
			new_thread.StartThread (do_attach);

			if ((mono_manager != null) && !do_attach)
				mono_manager.ThreadCreated (new_thread);

			OnThreadCreatedEvent (new_thread);
		}

		internal void ChildForked (Inferior inferior, int pid)
		{
			ProcessServant new_process = new ProcessServant (this, pid);

			Inferior new_inferior = Inferior.CreateInferior (
				manager, new_process, new_process.ProcessStart);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, new_process, new_inferior, pid);

			Report.Debug (DebugFlags.Threads, "Child forked: {0} {1}", pid, new_thread);

			new_process.main_thread = new_thread;

			// Order is important: first add the new engine to the manager's hash table,
			//                     then call inferior.Initialize() / inferior.Attach().
			manager.AddEngine (new_thread);
			new_thread.StartThread (false);

			new_inferior.InitializeAfterFork ();

			manager.Debugger.OnProcessCreatedEvent (new_process);
			new_process.OnThreadCreatedEvent (new_thread);
		}

		internal void ChildExecd (Inferior inferior)
		{
			if (!is_forked) {
				if (bfd_container != null)
					bfd_container.Dispose ();

				if (languages != null)
					foreach (ILanguageBackend lang in languages)
						lang.Dispose();

				if (symtab_manager != null)
					symtab_manager.Dispose ();
			}

			if (breakpoint_manager != null)
				breakpoint_manager.Dispose ();

			session = session.Clone (start.Options, "@" + id);
			session.OnProcessCreated (client);

			breakpoint_manager = new BreakpointManager ();

			symtab_manager = new SymbolTableManager (session);

			languages = new ArrayList ();
			bfd_container = new BfdContainer (this);

			Inferior new_inferior = Inferior.CreateInferior (manager, this, start);

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

			manager.ProcessExecd (new_thread);
			new_thread.StartThread (false);

			manager.Debugger.OnProcessExecdEvent (this);
			manager.Debugger.OnThreadCreatedEvent (new_thread.Thread);
			initialized = is_forked = false;

			inferior.Dispose ();

			Initialize (new_thread, new_inferior, true);
			new_inferior.Continue ();
		}

		internal void OnProcessExitedEvent ()
		{
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

		internal void OnThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
			thread.ThreadGroup.RemoveThread (thread.ID);
			session.DeleteThreadGroup (thread.ThreadGroup.Name);
			manager.Debugger.OnThreadExitedEvent (thread.Client);

			if (thread_hash.Count == 0)
				OnProcessExitedEvent ();
		}

		internal void WaitForApplication ()
		{
			initialized_event.WaitOne ();
		}

		internal void Initialize (SingleSteppingEngine engine, Inferior inferior,
					  bool is_exec)
		{
			if (initialized)
				return;

			if (!is_exec)
				inferior.InitializeProcess ();

			initialized = true;
			if (!is_forked || is_exec) {
				mono_manager = MonoThreadManager.Initialize (
					manager, inferior, (start.PID != 0) && !is_exec);
				if (!is_forked && !is_exec && !is_attached &&
				    !start.IsNative && (mono_manager == null))
					throw new TargetException (TargetError.CannotStartTarget,
								   "Unsupported `mono' executable: {0}",
								   start.TargetApplication);
			}

			this.main_thread = engine;

			if (thread_hash.Contains (engine.PID))
				thread_hash [engine.PID] = engine;
			else
				thread_hash.Add (engine.PID, engine);
			session.MainThreadGroup.AddThread (engine.Thread.ID);

			if (!is_forked && !is_exec)
				manager.Debugger.OnMainProcessCreatedEvent (this);

			if ((start.PID != 0) && !is_exec) {
				int[] threads = inferior.GetThreads ();
				foreach (int thread in threads) {
					if (thread_hash.Contains (thread))
						continue;
					ThreadCreated (inferior, thread, true);
				}
			}

			initialized_event.Set ();
		}

		public void Kill ()
		{
			main_thread.Kill ();
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

		// XXX This desperately needs to be renamed.
		internal ILanguageBackend CreateDebuggerHandler (MonoDebuggerInfo info)
		{
			mono_language = new MonoLanguageBackend (this, info);
			languages.Add (mono_language);

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
						"Cannot load .NET assembly {0} while " +
						"debugging an unmanaged application",
						filename);

			if (!mono_language.TryFindImage (thread, filename))
				bfd_container.AddFile (thread.TargetInfo, filename,
						       TargetAddress.Null, true, false);
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

			foreach (ThreadServant thread in thread_hash.Values) {
				if (thread == caller)
					continue;
				thread.ReleaseThreadLockDone ();
			}

			Report.Debug (DebugFlags.Threads,
				      "Released global thread lock #1: {0}", caller);
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
				if (bfd_container != null) {
					bfd_container.Dispose ();
					bfd_container = null;
				}

				if (languages != null) {
					foreach (ILanguageBackend lang in languages)
						lang.Dispose();
					languages = null;
				}

				if (symtab_manager != null) {
					symtab_manager.Dispose ();
					symtab_manager = null;
				}
			}

			if (breakpoint_manager != null)
				breakpoint_manager.Dispose ();

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
