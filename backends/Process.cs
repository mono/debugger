using System;
using System.Collections;
using ST = System.Threading;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger
{
	public class Process : MarshalByRefObject
	{
		ThreadManager manager;
		BfdContainer bfd_container;
		SymbolTableManager symtab_manager;
		ModuleManager module_manager;
		SourceFileFactory source_factory;
		MonoLanguageBackend mono_language;
		MonoThreadManager mono_manager;
		BreakpointManager breakpoint_manager;
		ProcessStart start;
		Thread main_thread;
		SingleSteppingEngine main_engine;
		ArrayList languages;
		Hashtable thread_hash;
		ArrayList attach_results;

		bool is_forked;
		bool initialized;
		ST.ManualResetEvent initialized_event;
		DebuggerMutex thread_lock_mutex;
		bool has_thread_lock;

		Hashtable thread_groups;
		ThreadGroup global_thread_group;
		ThreadGroup main_thread_group;

		int id = ++next_id;
		static int next_id = 0;

		private Process (ThreadManager manager)
		{
			this.manager = manager;

			thread_lock_mutex = new DebuggerMutex ("thread_lock_mutex");

			thread_groups = Hashtable.Synchronized (new Hashtable ());
			global_thread_group = CreateThreadGroup ("global");
			main_thread_group = CreateThreadGroup ("main");

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			initialized_event = new ST.ManualResetEvent (false);
		}

		internal Process (ThreadManager manager, ProcessStart start)
			: this (manager)
		{
			this.start = start;

			breakpoint_manager = new BreakpointManager ();
			module_manager = new ModuleManager ();
			source_factory = new SourceFileFactory ();

			module_manager.ModulesChanged += modules_changed;
			module_manager.BreakpointsChanged += breakpoints_changed;

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent += modules_reloaded;

			languages = new ArrayList ();
			bfd_container = new BfdContainer (this);
		}

		private Process (Process parent, int pid)
			: this (parent.manager)
		{
			this.start = new ProcessStart (parent.ProcessStart, pid);

			this.is_forked = true;
			this.initialized = true;

			breakpoint_manager = new BreakpointManager (parent.breakpoint_manager);
			module_manager = parent.module_manager;
			source_factory = parent.source_factory;

			module_manager.ModulesChanged += modules_changed;
			module_manager.BreakpointsChanged += breakpoints_changed;

			symtab_manager = parent.symtab_manager;
			symtab_manager.ModulesChangedEvent += modules_reloaded;

			languages = parent.languages;
			bfd_container = parent.bfd_container;
		}

		public int ID {
			get { return id; }
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

		public ModuleManager ModuleManager {
			get {
				return module_manager;
			}
		}

		public SymbolTableManager SymbolTableManager {
			get {
				return symtab_manager;
			}
		}

		public SourceFileFactory SourceFileFactory {
			get {
				return source_factory;
			}
		}

		public Language NativeLanguage {
			get {
				return bfd_container.NativeLanguage;
			}
		}

		public Thread MainThread {
			get { return main_thread; }
		}

		public Debugger Debugger {
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

		public event ModulesChangedHandler ModulesChangedEvent;
		public event BreakpointsChangedHandler BreakpointsChangedEvent;

		void modules_changed ()
		{
			check_disposed ();
			symtab_manager.SetModules (module_manager.Modules);
		}

		Module[] current_modules = null;

		void breakpoints_changed ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent ();
		}

		void modules_reloaded (object sender, Module[] modules)
		{
			current_modules = modules;

			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		internal void ReachedMain (Inferior inferior, Thread thread,
					   SingleSteppingEngine engine)
		{
			manager.Debugger.OnProcessReachedMainEvent (this);

			module_manager.UnLock ();
			symtab_manager.Wait ();

			inferior.InitializeModules ();
		}

		internal void ThreadCreated (Inferior inferior, int pid, bool do_attach)
		{
			Inferior new_inferior = inferior.CreateThread ();

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, this, new_inferior, pid, do_attach, false);

			Report.Debug (DebugFlags.Threads, "Thread created: {0} {1}", pid, new_thread);

			manager.AddEngine (new_thread);

			if ((mono_manager != null) && !do_attach &&
			    mono_manager.ThreadCreated (new_thread, new_inferior, inferior)) {
				// main_process = new_thread.Process;
				// main_engine = new_thread;
			}

			OnThreadCreatedEvent (new_thread.Thread);

			if (!do_attach)
				new_thread.Start (TargetAddress.Null);
		}

		internal void ChildForked (Inferior inferior, int pid)
		{
			Process new_process = new Process (this, pid);

			Inferior new_inferior = Inferior.CreateInferior (
				manager, new_process, new_process.ProcessStart);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, new_process, new_inferior, pid, false, false);

			new_inferior.InitializeAfterFork ();

			Report.Debug (DebugFlags.Threads, "Child forked: {0} {1}", pid, new_thread);

			new_process.main_thread = new_thread.Thread;
			new_process.main_engine = new_thread;

			manager.Debugger.OnProcessCreatedEvent (new_process);
			new_process.OnThreadCreatedEvent (new_thread.Thread);

			manager.AddEngine (new_thread);
			new_thread.Start (TargetAddress.Null);
		}

		internal void ChildExecd (Inferior inferior)
		{
			TargetAddress main_method = inferior.MainMethodAddress;

			Report.Debug (DebugFlags.Threads, "Child execd: {0} {1} {2}",
				      inferior.PID, start, main_method);

			thread_hash.Remove (inferior.PID);

			Process new_process = new Process (manager, start);

			Inferior new_inferior = Inferior.CreateInferior (manager, new_process, start);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (
				manager, new_process, new_inferior, inferior.PID, false, true);

			Report.Debug (DebugFlags.Threads, "Child execd: {0} {1}",
				      inferior.PID, new_thread);

			new_process.main_thread = new_thread.Thread;
			new_process.main_engine = new_thread;

			manager.Debugger.OnProcessCreatedEvent (new_process);
			new_process.OnThreadCreatedEvent (new_thread.Thread);

			manager.ProcessExecd (new_thread);
			if (new_process.Initialize (new_thread, new_inferior))
				new_inferior.Continue ();
		}
		
		protected void OnThreadCreatedEvent (Thread thread)
		{
			thread_hash.Add (thread.PID, thread);
			manager.Debugger.OnThreadCreatedEvent (thread);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			manager.Debugger.OnThreadExitedEvent (thread);
			thread.Kill ();
		}

		internal void WaitForApplication ()
		{
			initialized_event.WaitOne ();
		}

		internal bool Initialize (SingleSteppingEngine engine, Inferior inferior)
		{
			if (initialized)
				return true;

			initialized = true;
			if (!is_forked)
				mono_manager = MonoThreadManager.Initialize (
					manager, inferior, start.PID != 0);

			this.main_thread = engine.Thread;
			this.main_engine = engine;

			if (thread_hash.Contains (engine.PID))
				thread_hash [engine.PID] = engine.Thread;
			else
				thread_hash.Add (engine.PID, engine.Thread);
			main_thread_group.AddThread (engine.Thread.ID);

			if (start.PID != 0) {
				int[] threads = inferior.GetThreads ();
				foreach (int thread in threads) {
					if (thread_hash.Contains (thread))
						continue;
					ThreadCreated (inferior, thread, true);
				}

				if (mono_manager == null)
					goto done;

				attach_results = new ArrayList ();
				foreach (Thread thread in thread_hash.Values)
					attach_results.Add (mono_manager.GetThreadID (thread));
			}

			if (mono_manager != null) {
				inferior.Continue ();
				goto done;
			}

			TargetAddress main_method = inferior.MainMethodAddress;
			engine.Start (main_method);

		done:
			initialized_event.Set ();
			return false;
		}

		internal void Kill ()
		{
			Thread[] threads;
			lock (thread_hash.SyncRoot) {
				threads = new Thread [thread_hash.Count];
				thread_hash.Values.CopyTo (threads, 0);
			}

			for (int i = 0; i < threads.Length; i++)
				threads [i].Kill ();

			Dispose ();
		}

		internal void Kill (SingleSteppingEngine engine)
		{
			if (engine == main_engine) {
				manager.Debugger.OnProcessExitedEvent (main_engine.Process);
				Kill ();
			} else {
				thread_hash.Remove (engine.PID);
				OnThreadExitedEvent (engine.Thread);
				engine.Thread.Kill ();
			}
		}

		// XXX This desperately needs to be renamed.
		internal ILanguageBackend CreateDebuggerHandler (MonoDebuggerInfo info)
		{
			mono_language = new MonoLanguageBackend (this, info);
			languages.Add (mono_language);

			return mono_language;
		}

		internal void UpdateSymbolTable (ITargetMemoryAccess target)
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
				bfd_container.AddFile (thread, filename, TargetAddress.Null, true, false);
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
			get {
				if (current_modules != null)
					return current_modules;

				return new Module [0];
			}
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
				SourceMethod method = module.FindMethod (name);
				
				if (method != null)
					return new SourceLocation (method);
			}

			return null;
		}

		public Thread[] Threads {
			get {
				lock (thread_hash.SyncRoot) {
					Thread[] threads = new Thread [thread_hash.Count];
					thread_hash.Values.CopyTo (threads, 0);
					return threads;
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
			foreach (Thread thread in thread_hash.Values) {
				if (thread.Engine == caller)
					continue;
				thread.Engine.AcquireThreadLock ();
			}
			Report.Debug (DebugFlags.Threads,
				      "Done acquiring global thread lock: {0}",
				      caller);
		}

		internal void ReleaseGlobalThreadLock (SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Releasing global thread lock: {0}", caller);
				
			foreach (Thread thread in thread_hash.Values) {
				if (thread.Engine == caller)
					continue;
				thread.Engine.ReleaseThreadLock ();
			}
			has_thread_lock = false;
			thread_lock_mutex.Unlock ();
			Report.Debug (DebugFlags.Threads,
				      "Released global thread lock: {0}", caller);
		}

		//
		// Thread Groups
		//

		public ThreadGroup CreateThreadGroup (string name)
		{
			lock (thread_groups) {
				ThreadGroup group = (ThreadGroup) thread_groups [name];
				if (group != null)
					return group;

				group = new ThreadGroup (name);
				thread_groups.Add (name, group);
				return group;
			}
		}

		public void DeleteThreadGroup (string name)
		{
			thread_groups.Remove (name);
		}

		public bool ThreadGroupExists (string name)
		{
			return thread_groups.Contains (name);
		}

		public ThreadGroup[] ThreadGroups {
			get {
				lock (thread_groups) {
					ThreadGroup[] retval = new ThreadGroup [thread_groups.Values.Count];
					thread_groups.Values.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public ThreadGroup ThreadGroupByName (string name)
		{
			return (ThreadGroup) thread_groups [name];
		}

		public ThreadGroup MainThreadGroup {
			get { return main_thread_group; }
		}

		public ThreadGroup GlobalThreadGroup {
			get { return global_thread_group; }
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

			if (!is_forked && (breakpoint_manager != null))
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

		~Process ()
		{
			Dispose (false);
		}
	}
}
