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
using System.Runtime.Serialization;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public delegate void MethodInvalidHandler ();
	public delegate void MethodChangedHandler (IMethod method);

	[Serializable]
	public class DebuggerBackend : IDisposable, ISerializable, IDeserializationCallback
	{
		BfdContainer bfd_container;

		ArrayList languages;
		SourceFileFactory source_factory;
		MonoCSharpLanguageBackend csharp_language;
		SymbolTableManager symtab_manager;
		ModuleManager module_manager;
		ThreadManager thread_manager;
		ThreadGroup main_group;
		Process process;
		ProcessStart start;

		public DebuggerBackend ()
		{
			module_manager = new ModuleManager ();
			main_group = ThreadGroup.CreateThreadGroup ("main");

			Initialize ();
		}

		protected void Initialize ()
		{
			if (initialized)
				throw new InternalError ();
			initialized = true;

			source_factory = new SourceFileFactory ();

			languages = new ArrayList ();
			bfd_container = new BfdContainer (this);
			thread_manager = new ThreadManager (this, bfd_container);

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent +=
				new SymbolTableManager.ModuleHandler (modules_reloaded);

			module_manager.ModulesChanged += new ModulesChangedHandler (modules_changed);
			module_manager.BreakpointsChanged += new BreakpointsChangedHandler (breakpoints_changed);
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

		public event TargetExitedHandler TargetExited;
		public event SymbolTableChangedHandler SymbolTableChanged;

		public event ModulesChangedHandler ModulesChangedEvent;
		public event BreakpointsChangedHandler BreakpointsChangedEvent;

		void process_exited (Process process)
		{
			this.process = null;
			if (TargetExited != null)
				TargetExited ();
		}

		void debugger_error (object sender, string message, Exception e)
		{
		}

		public Process Run (ProcessStart start)
		{
			check_disposed ();

			this.start = start;

			if (process != null)
				throw new AlreadyHaveTargetException ();

			module_manager.Lock ();

			process = thread_manager.StartApplication (this, start, bfd_container);
			process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);

			main_group.AddThread (process);
			return process;
		}

		internal void InitializeCoreFile (Process process, CoreFile core)
		{
			if (!process.ProcessStart.IsNative) {
				csharp_language = new MonoCSharpLanguageBackend (this, process, core);
				languages.Add (csharp_language);
				symtab_manager.SetModules (module_manager.Modules);
				core.UpdateModules ();
			}
		}

		public Process ReadCoreFile (ProcessStart start, string core_file)
		{
			check_disposed ();

			if (process != null)
				throw new AlreadyHaveTargetException ();

			process = new Process (this, start, bfd_container, core_file);
			return process;
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

		internal void ReachedMain (Process process, IInferior inferior)
		{
			inferior.UpdateModules ();

			foreach (Module module in Modules)
				module.BackendLoaded = true;

			thread_manager.Initialize (process, inferior);

			module_manager.UnLock ();
			symtab_manager.Wait ();
		}

		internal Process CreateDebuggerProcess (Process command_process, int pid)
		{
			csharp_language = new MonoCSharpLanguageBackend (this, command_process);
			languages.Add (csharp_language);

			return command_process.CreateDaemonThread (
				pid, 0, new DaemonThreadHandler (csharp_language.DaemonThreadHandler));
		}

		internal void ReachedManagedMain (Process process)
		{
			foreach (Module module in Modules)
				module.BackendLoaded = true;

			module_manager.UnLock ();
			symtab_manager.Wait ();
		}

		public void Quit ()
		{
			if (process != null)
				process.Kill ();
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

		public SourceLocation FindLocation (string name)
		{
			foreach (Module module in Modules) {
				SourceMethod method = module.FindMethod (name);
				
				if (method != null)
					return new SourceLocation (method);
			}

			return null;
		}

		void method_loaded (SourceMethod method, object user_data)
		{
			Console.WriteLine ("METHOD LOADED: {0}", method);
		}

		Hashtable breakpoint_map = new Hashtable ();

		public int InsertBreakpoint (Breakpoint breakpoint, SourceLocation location)
		{
			return InsertBreakpoint (breakpoint, main_group, location);
		}

		// <summary>
		//   Inserts a breakpoint for method @method.
		// </summary>
		public int InsertBreakpoint (Breakpoint breakpoint, ThreadGroup group,
					     SourceLocation location)
		{
			int index = location.InsertBreakpoint (breakpoint, group);
			breakpoint_map [index] = location;
			return index;
		}

		public void RemoveBreakpoint (int index)
		{
			if (breakpoint_map [index] == null)
				throw new Exception ("Breakpoint is not registered");

			SourceLocation location = (SourceLocation) breakpoint_map [index];
			location.RemoveBreakpoint (index);
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

		public bool BreakpointHit (IInferior inferior, TargetAddress address)
		{
			foreach (ILanguageBackend language in languages) {
				if (!language.BreakpointHit (inferior, address))
					return false;
			}

			return true;
		}

		public bool SignalHandler (Process process, IInferior inferior, int signal)
		{
			bool action;
			if (thread_manager.SignalHandler (inferior, signal, out action))
				return action;

			return true;
		}

		//
		// IDeserializationCallback
		//

		bool initialized = false;

		public void OnDeserialization (object sender)
		{
			Initialize ();

			symtab_manager.SetModules (module_manager.Modules);
		}

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("modules", module_manager);
			info.AddValue ("start", start);
			info.AddValue ("main_group", main_group);
		}

		protected DebuggerBackend (SerializationInfo info, StreamingContext context)
		{
			module_manager = (ModuleManager) info.GetValue ("modules", typeof (ModuleManager));
			start = (ProcessStart) info.GetValue ("start", typeof (ProcessStart));
			main_group = (ThreadGroup) info.GetValue ("main_group", typeof (ThreadGroup));
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Debugger");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					if (symtab_manager != null)
						symtab_manager.Dispose ();
					if (process != null)
						process.Dispose ();
					thread_manager.Dispose ();
					bfd_container.Dispose ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					// Nothing to do yet.
				}
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
