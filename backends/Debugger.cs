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
using System.Runtime.Remoting;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	public delegate void DebuggerEventHandler (Debugger debugger, Thread thread);

	public abstract class Debugger : MarshalByRefObject, IDisposable
	{
		BfdContainer bfd_container;

		ArrayList languages;
		DebuggerManager manager;
		SourceFileFactory source_factory;
		MonoLanguageBackend mono_language;
		SymbolTableManager symtab_manager;
		ModuleManager module_manager;
		ThreadManager thread_manager;
		ProcessStart start;

		protected Debugger (DebuggerManager manager)
		{
			this.manager = manager;

			DebuggerContext.CreateServerContext (this);

			module_manager = new ModuleManager ();

			source_factory = new SourceFileFactory ();

			languages = new ArrayList ();
			bfd_container = new BfdContainer (this);

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent +=
				new SymbolTableManager.ModuleHandler (modules_reloaded);

			module_manager.ModulesChanged += new ModulesChangedHandler (modules_changed);
			module_manager.BreakpointsChanged += new BreakpointsChangedHandler (breakpoints_changed);

			thread_manager = new ThreadManager (this);
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

		internal ThreadManager ThreadManager {
			get {
				return thread_manager;
			}
		}

		internal ProcessStart ProcessStart {
			get {
				return start;
			}
		}

		public SourceFileFactory SourceFileFactory {
			get {
				return source_factory;
			}
		}

		internal BfdContainer BfdContainer {
			get {
				return bfd_container;
			}
		}

		public Language NativeLanguage {
			get {
				return bfd_container.NativeLanguage;
			}
		}

		public event DebuggerEventHandler InitializedEvent;
		public event DebuggerEventHandler MainThreadCreatedEvent;
		public event DebuggerEventHandler ThreadCreatedEvent;
		public event DebuggerEventHandler ThreadExitedEvent;
		public event TargetExitedHandler TargetExitedEvent;

		public event TargetEventHandler TargetEvent;

		public event SymbolTableChangedHandler SymbolTableChanged;

		public event ModulesChangedHandler ModulesChangedEvent;
		public event BreakpointsChangedHandler BreakpointsChangedEvent;

		public void Run (DebuggerOptions options)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = new ProcessStart (options);

			module_manager.Lock ();

			thread_manager.StartApplication (start);
		}

		public void Attach (DebuggerOptions options, int pid)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = new ProcessStart (options, pid);

			module_manager.Lock ();

			thread_manager.StartApplication (start);
		}

		public Thread OpenCoreFile (DebuggerOptions options, string core_file,
					    out Thread[] threads)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = new ProcessStart (options, core_file);

			module_manager.Lock ();

			return thread_manager.OpenCoreFile (start, out threads);
		}

		public Thread WaitForApplication ()
		{
			return thread_manager.WaitForApplication ();
		}

		internal void OnInitializedEvent (Thread main_process)
		{
			manager.MainThreadGroup.AddThread (main_process.ID);
			if (InitializedEvent != null)
				InitializedEvent (this, main_process);
		}

		internal void OnMainThreadCreatedEvent (Thread new_process)
		{
			if (MainThreadCreatedEvent != null)
				MainThreadCreatedEvent (this, new_process);
		}

		internal void OnThreadCreatedEvent (Thread new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, thread);
		}

		internal void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		internal void SendTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			try {
				if (TargetEvent != null)
					TargetEvent (sse.TargetAccess, args);
			} catch (Exception ex) {
				Error ("{0} caught exception while sending {1}:\n{2}",
				       sse, args, ex);
			}
		}

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

		// XXX This desperately needs to be renamed.
		internal ILanguageBackend CreateDebuggerHandler (MonoDebuggerInfo info)
		{
			mono_language = new MonoLanguageBackend (this, info);
			languages.Add (mono_language);

			return mono_language;
		}

		internal void AddLanguage (ILanguageBackend language)
		{
			languages.Add (language);
		}

		internal ArrayList Languages {
			get { return languages; }
		}

		internal DebuggerManager DebuggerManager {
			get { return manager; }
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

		public void Error (string message, params object[] args)
		{
			Console.WriteLine ("ERROR: " + String.Format (message, args));
		}

		internal void UpdateSymbolTable (ITargetMemoryAccess target)
		{
			if (mono_language != null)
				mono_language.Update (target);
		}

		public Module[] Modules {
			get {
				if (current_modules != null)
					return current_modules;

				return new Module [0];
			}
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

		public EventHandle InsertBreakpoint (TargetAccess target, int domain,
						     Breakpoint bpt, SourceLocation location)
		{
			return new BreakpointHandle (target, domain, bpt, location);
		}

		public EventHandle InsertBreakpoint (TargetAccess target, Breakpoint bpt,
						     TargetFunctionType func)
		{
			return new BreakpointHandle (target, bpt, func);
		}

		public EventHandle InsertExceptionCatchPoint (TargetAccess target, ThreadGroup group,
							      TargetType exception)
		{
			return new CatchpointHandle (target, group, exception);
		}

		//
		// IDisposable
		//

		protected abstract void DebuggerExited ();

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Debugger");
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
				if (symtab_manager != null) {
					symtab_manager.Dispose ();
					symtab_manager = null;
				}
				if (thread_manager != null) {
					thread_manager.Dispose ();
					thread_manager = null;
				}
				if (bfd_container != null) {
					bfd_container.Dispose ();
					bfd_container = null;
				}
				if (languages != null) {
					foreach (ILanguageBackend lang in languages)
						lang.Dispose();
					languages = null;
				}

				ObjectCache.Shutdown ();

				DebuggerExited ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Debugger ()
		{
			Dispose (false);
		}
	}
}
