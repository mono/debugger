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
using Mono.Debugger.Architecture;
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	public abstract class DebuggerBackend : MarshalByRefObject, IDisposable
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

		protected DebuggerBackend (DebuggerManager manager)
		{
			this.manager = manager;

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
			thread_manager.InitializedEvent += new ThreadEventHandler (initialized_event);
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

		public ThreadManager ThreadManager {
			get {
				return thread_manager;
			}
		}

		public ProcessStart ProcessStart {
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

		public event SymbolTableChangedHandler SymbolTableChanged;

		public event ModulesChangedHandler ModulesChangedEvent;
		public event BreakpointsChangedHandler BreakpointsChangedEvent;

		public ProcessStart Run (DebuggerOptions options, string[] argv)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = ProcessStart.Create (options, argv);

			module_manager.Lock ();

			thread_manager.StartApplication (start);
			return start;
		}

		void initialized_event (ThreadManager manager, Process process)
		{
			ThreadGroup.Main.AddThread (process.ID);
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
		internal ILanguageBackend CreateDebuggerHandler ()
		{
			mono_language = new MonoLanguageBackend (this);
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

		public void UpdateSymbolTable ()
		{ }

		public Module[] Modules {
			get {
				if (current_modules != null)
					return current_modules;

				return new Module [0];
			}
		}

		public void LoadLibrary (Process process, string filename)
		{
			if (mono_language == null)
				throw new SymbolTableException (
						"Cannot load .NET assembly {0} while " +
						"debugging an unmanaged application",
						filename);

			if (!mono_language.TryFindImage (process, filename))
				bfd_container.AddFile (process, filename, true, false, false);
		}

		internal MonoLanguageBackend MonoLanguage {
			get {
				if (mono_language == null)
					throw new InvalidOperationException ();

				return mono_language;
			}
		}

		public EventHandle InsertBreakpoint (Process process, Breakpoint bpt,
						     SourceLocation location)
		{
			return new BreakpointHandle (process, bpt, location);
		}

		public EventHandle InsertBreakpoint (Process process, Breakpoint bpt,
						     ITargetFunctionType func)
		{
			return new BreakpointHandle (process, bpt, func);
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

		~DebuggerBackend ()
		{
			Dispose (false);
		}
	}
}
