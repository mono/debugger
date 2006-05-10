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
		ModuleManager module_manager;
		SourceFileFactory source_factory;
		MonoLanguageBackend mono_language;
		MonoThreadManager mono_manager;
		BreakpointManager breakpoint_manager;
		ProcessStart start;
		protected ThreadServant main_thread;
		ArrayList languages;
		Hashtable thread_hash;
		Hashtable events;

		bool is_attached;
		bool is_forked;
		bool initialized;
		ST.ManualResetEvent initialized_event;
		DebuggerMutex thread_lock_mutex;
		bool has_thread_lock;

		Hashtable thread_groups;
		ThreadGroup main_thread_group;

		int id = ++next_id;
		static int next_id = 0;

		private ProcessServant (ThreadManager manager)
		{
			this.manager = manager;
			this.client = manager.Debugger.Client.CreateProcess (this);

			thread_lock_mutex = new DebuggerMutex ("thread_lock_mutex");

			thread_groups = Hashtable.Synchronized (new Hashtable ());
			main_thread_group = CreateThreadGroup ("main");

			events = Hashtable.Synchronized (new Hashtable ());
			thread_hash = Hashtable.Synchronized (new Hashtable ());
			initialized_event = new ST.ManualResetEvent (false);
		}

		internal ProcessServant (ThreadManager manager, ProcessStart start)
			: this (manager)
		{
			this.start = start;

			is_attached = start.PID != 0;

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

		private ProcessServant (ProcessServant parent, int pid)
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

		public bool IsAttached {
			get { return is_attached; }
		}

		public Process Client {
			get { return client; }
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

		internal ModuleManager ModuleManager {
			get {
				return module_manager;
			}
		}

		internal SymbolTableManager SymbolTableManager {
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

		internal void ReachedMain ()
		{
			module_manager.UnLock ();
			symtab_manager.Wait ();
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

			module_manager.ModulesChanged -= modules_changed;
			module_manager.BreakpointsChanged -= breakpoints_changed;

			if (breakpoint_manager != null)
				breakpoint_manager.Dispose ();

			breakpoint_manager = new BreakpointManager ();
			module_manager = new ModuleManager ();
			source_factory = new SourceFileFactory ();

			module_manager.ModulesChanged += modules_changed;
			module_manager.BreakpointsChanged += breakpoints_changed;

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent += modules_reloaded;

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

		protected void OnThreadCreatedEvent (ThreadServant thread)
		{
			thread_hash.Add (thread.PID, thread);
			manager.Debugger.OnThreadCreatedEvent (thread.Client);
		}

		internal void OnThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
			manager.Debugger.OnThreadExitedEvent (thread.Client);

			if (thread_hash.Count == 0)
				manager.Debugger.OnProcessExitedEvent (this);
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
			if (!is_forked || is_exec)
				mono_manager = MonoThreadManager.Initialize (
					manager, inferior, (start.PID != 0) && !is_exec,
					!is_exec);

			this.main_thread = engine;

			if (thread_hash.Contains (engine.PID))
				thread_hash [engine.PID] = engine;
			else
				thread_hash.Add (engine.PID, engine);
			main_thread_group.AddThread (engine.Thread.ID);

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
			Dispose ();
		}

		public void Detach ()
		{
			if (!IsAttached)
				throw new TargetException (TargetError.CannotDetach);

			main_thread.Detach ();
		}

		internal void OnTargetDetached ()
		{
			manager.Debugger.OnProcessExitedEvent (this);
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

		public Thread[] Threads {
			get {
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
		}

		//
		// Session management.
		//

		public void SaveSession (Stream stream, StreamingContextStates states )
		{
			StreamingContext context = new StreamingContext (
				states, this);

			ISurrogateSelector ss = DebuggerSession.CreateSurrogateSelector (context);
			BinaryFormatter formatter = new BinaryFormatter (ss, context);

			SessionInfo info = new SessionInfo (this);
			formatter.Serialize (stream, info);
		}

		public void LoadSession (Stream stream, StreamingContextStates states)
		{
			StreamingContext context = new StreamingContext (
				StreamingContextStates.Persistence, this);

			ISurrogateSelector ss = DebuggerSession.CreateSurrogateSelector (context);
			BinaryFormatter formatter = new BinaryFormatter (ss, context);

			SessionInfo info = (SessionInfo) formatter.Deserialize (stream);

			foreach (Event handle in info.Events) {
				AddEvent (handle);
				handle.Enable (MainThread.Client);
			}
		}

		[Serializable]
		private class SessionInfo : ISerializable, IDeserializationCallback
		{
			public readonly Module[] Modules;
			public readonly Event[] Events;

			public SessionInfo (ProcessServant process)
			{
				this.Modules = process.Modules;
				this.Events = process.Events;
			}

			public void GetObjectData (SerializationInfo info, StreamingContext context)
			{
				info.AddValue ("modules", Modules);
				info.AddValue ("events", Events);
			}

			void IDeserializationCallback.OnDeserialization (object obj)
			{ }

			private SessionInfo (SerializationInfo info, StreamingContext context)
			{
				Modules = (Module []) info.GetValue (
					"modules", typeof (Module []));
				Events = (Event []) info.GetValue (
					"events", typeof (Event []));
			}
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

				group = ThreadGroup.CreateThreadGroup (name);
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

		//
		// Events
		//

		public Event[] Events {
			get {
				Event[] handles = new Event [events.Count];
				events.Values.CopyTo (handles, 0);
				return handles;
			}
		}

		public Event GetEvent (int index)
		{
			return (Event) events [index];
		}

		internal void AddEvent (Event handle)
		{
			events.Add (handle.Index, handle);
		}

		public void DeleteEvent (Thread thread, Event handle)
		{
			handle.Remove (thread);
			events.Remove (handle.Index);
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group, int domain,
					       SourceLocation location)
		{
			Event handle = new Breakpoint (group, location);
			events.Add (handle.Index, handle);
			return handle;
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       TargetFunctionType func)
		{
			Event handle = new Breakpoint (group, new SourceLocation (func));
			events.Add (handle.Index, handle);
			return handle;
		}

		public Event InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							TargetType exception)
		{
			Event handle = new ExceptionCatchPoint (group, exception);
			events.Add (handle.Index, handle);
			return handle;
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
