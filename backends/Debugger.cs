using GLib;
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
	public delegate void MethodInvalidHandler ();
	public delegate void MethodChangedHandler (IMethod method);

	public class DebuggerBackend : IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		BfdContainer bfd_container;

		IInferior inferior;
		CoreFile core;
		ArrayList languages;
		MonoCSharpLanguageBackend csharp_language;
		SingleSteppingEngine sse;
		SymbolTableManager symtab_manager;
		ModuleManager module_manager;
		Process process;

		string[] argv;
		string[] envp;
		string target_application;
		string working_directory;

		bool load_native_symtab = false;

		bool native;

		public DebuggerBackend ()
			: this (false)
		{ }

		public DebuggerBackend (bool native)
		{
			NameValueCollection settings = ConfigurationSettings.AppSettings;

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "mono-path":
					Path_Mono = value;
					break;

				case "environment-path":
					Environment_Path = value;
					break;

				default:
					break;
				}
			}

			this.native = native;
			this.languages = new ArrayList ();
			this.module_manager = new ModuleManager ();
			this.bfd_container = new BfdContainer (this);

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent +=
				new SymbolTableManager.ModuleHandler (modules_reloaded);

			csharp_language = new MonoCSharpLanguageBackend (this);
			module_manager.ModulesChanged += new ModulesChangedHandler (modules_changed);
			module_manager.BreakpointsChanged += new BreakpointsChangedHandler (breakpoints_changed);
			languages.Add (csharp_language);
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

		public string CurrentWorkingDirectory {
			get {
				return working_directory;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				working_directory = value;
			}
		}

		public string[] CommandLineArguments {
			get {
				return argv;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				argv = value;
			}
		}

		public string TargetApplication {
			get {
				return target_application;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				target_application = value;
			}
		}

		public string[] Environment {
			get {
				return envp;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				envp = value;
			}
		}

		public SingleSteppingEngine SingleSteppingEngine {
			get {
				return sse;
			}
		}

		// <remarks>
		//   This is a temporary solution during the DebuggerBackend -> Process migration.
		// </remarks>
		public Process CurrentProcess {
			get {
				Console.WriteLine ("WARNING: DebuggerBackend.CurrentProcess is " +
						   "only a temporary solution and will go away soon!");

				if (process == null)
					throw new NoTargetException ();

				return process;
			}
		}

		// <summary>
		//   If true, load the target's native symbol table.  You need to enable this
		//   to debug native C applications, but you can safely disable it if you just
		//   want to debug managed C# code.
		// </summary>
		public bool LoadNativeSymbolTable {
			get {
				return load_native_symtab;
			}

			set {
				load_native_symtab = value;
			}
		}

		//
		// ITargetNotification
		//

		bool busy = false;
		public TargetState State {
			get {
				if (busy)
					return TargetState.BUSY;
				else if (inferior == null)
					return TargetState.NO_TARGET;
				else
					return inferior.State;
			}
		}

		bool DebuggerBusy {
			get {
				return busy;
			}

			set {
				if (busy == value)
					return;

				busy = value;
				if (StateChanged != null)
					StateChanged (State, 0);
			}
		}

		void target_state_changed (TargetState new_state, int arg)
		{
			if (new_state == TargetState.STOPPED) {
				if (busy) {
					busy = false;
					return;
				}
			}

			if (new_state == TargetState.BUSY) {
				busy = true;
				return;
			}

			busy = false;

			if (StateChanged != null)
				StateChanged (new_state, arg);
		}

		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;
		public event SymbolTableChangedHandler SymbolTableChanged;

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;
		public event ModulesChangedHandler ModulesChangedEvent;
		public event BreakpointsChangedHandler BreakpointsChangedEvent;

		public IInferior Inferior {
			get {
				check_disposed ();
				return inferior;
			}
		}

		public bool HasTarget {
			get {
				check_disposed ();
				return inferior != null;
			}
		}

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;

			sse = null;
			core = null;
			process = null;

			frames_invalid ();	
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
			if (TargetExited != null)
				TargetExited ();
		}

		void debugger_error (object sender, string message, Exception e)
		{
		}

		public Process Run (ProcessStart start)
		{
			check_disposed ();

			if (inferior != null)
				throw new AlreadyHaveTargetException ();

			module_manager.Locked = true;

			process = new Process (this, start, bfd_container);
			inferior = process.Inferior;
			inferior.TargetExited += new TargetExitedHandler (child_exited);

			if (!start.IsNative)
				csharp_language.Inferior = inferior;

			sse = process.SingleSteppingEngine;
			sse.StateChangedEvent += new StateChangedHandler (target_state_changed);
			sse.MethodInvalidEvent += new MethodInvalidHandler (method_invalid);
			sse.MethodChangedEvent += new MethodChangedHandler (method_changed);
			sse.FrameChangedEvent += new StackFrameHandler (frame_changed);
			sse.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);

			return process;
		}

		public Process ReadCoreFile (ProcessStart start, string core_file)
		{
			check_disposed ();

			if (inferior != null)
				throw new AlreadyHaveTargetException ();

			process = new Process (this, start, bfd_container, core_file);
			inferior = process.Inferior;
			core = inferior as CoreFile;

			if (!start.IsNative)
				csharp_language.Inferior = inferior;

			Reload ();

			return process;
		}

		void method_invalid ()
		{
			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
		}

		void method_changed (IMethod method)
		{
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}

		void frame_changed (StackFrame frame)
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		void frames_invalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
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
			Console.WriteLine ("MODULES RELOADED");

			current_modules = modules;

			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		internal void ReachedMain ()
		{
			module_manager.Locked = true;
			inferior.UpdateModules ();
			UpdateSymbolTable ();

			foreach (Module module in Modules)
				module.BackendLoaded = true;

			module_manager.Locked = false;
		}

		internal void ChildExited ()
		{
			module_manager.UnLoadAllModules ();
		}

		public void Quit ()
		{
			if (inferior != null)
				inferior.Shutdown ();
		}

		bool check_inferior ()
		{
			check_disposed ();
			if (inferior == null){
				// throw new NoTargetException ();
				return false;
			}
			return true;
		}

		bool check_stopped ()
		{
			check_inferior ();

			if ((State != TargetState.STOPPED) && (State != TargetState.CORE_FILE))
				//throw new TargetNotStoppedException ();
				return false;

			return true;
		}

		bool check_can_run ()
		{
			if (!check_inferior ())
				return false;

			if (false){
				if (sse == null)
					throw new CannotExecuteCoreFileException ();
				
				if (State == TargetState.CORE_FILE)
					throw new CannotExecuteCoreFileException ();
				else if (State != TargetState.STOPPED)
					throw new TargetNotStoppedException ();

				return true;
			} else {
				if (sse == null || State == TargetState.CORE_FILE){
					Console.WriteLine ("Error: Cannot Execute CoreFile");
					return false;
				} else if (State != TargetState.STOPPED) {
					Console.WriteLine ("The target is not stopped");
					return false;
				}
				return true;
			}
		}

		SourceMethodInfo FindMethod (string name)
		{
			foreach (Module module in Modules) {
				SourceMethodInfo method = module.FindMethod (name);
				
				if (method != null)
					return method;
			}

			return null;
		}

		SourceMethodInfo FindMethod (string source, int line)
		{
			foreach (Module module in Modules) {
				SourceMethodInfo method = module.FindMethod (source, line);
				
				if (method != null)
					return method;
			}

			return null;
		}

		void method_loaded (SourceMethodInfo method, object user_data)
		{
			Console.WriteLine ("METHOD LOADED: {0}", method);
		}

		Hashtable breakpoint_module_map = new Hashtable ();
		
		// <summary>
		//   Inserts a breakpoint for method @name, which must be the method's full
		//   name, including the signature.
		//
		//   Example:
		//     System.DateTime.GetUtcOffset(System.DateTime)
		// </summary>
		public int InsertBreakpoint (Breakpoint breakpoint, string name)
		{
			SourceMethodInfo method = FindMethod (name);
			if (method == null) {
				Console.WriteLine ("Can't find any method with this name.");
				return -1;
			}

			Console.WriteLine ("METHOD: {0} {1} {2}", method, method.SourceInfo,
					   method.SourceInfo.Module);

			Module module = method.SourceInfo.Module;

			int index = module.AddBreakpoint (breakpoint, method);
			breakpoint_module_map [index] = module;
			Console.WriteLine ("BREAKPOINT INSERTED: {0}", index);
			return index;
		}

		// <summary>
		//   Inserts a breakpoint at source file @source (while must be a full pathname)
		//   and line @line.
		// </summary>
		public int InsertBreakpoint (Breakpoint breakpoint, string source, int line)
		{
			SourceMethodInfo method = FindMethod (source, line);
			if (method == null) {
				Console.WriteLine ("No method contains this line.");
				return -1;
			}

			Console.WriteLine ("METHOD: {0} {1} {2}", method, method.SourceInfo,
					   method.SourceInfo.Module);

			Module module = method.SourceInfo.Module;

			int index = module.AddBreakpoint (breakpoint, method, line);
			breakpoint_module_map [index] = module;
			Console.WriteLine ("BREAKPOINT INSERTED: {0}", index);
			return index;
		}

		public void RemoveBreakpoint (int index)
		{
			if (breakpoint_module_map [index] == null)
				throw new Exception ("Breakpoint is not registered");

			Module mod = (Module) breakpoint_module_map [index];
			mod.RemoveBreakpoint (index);
		}

		public TargetAddress CurrentFrameAddress {
			get {
				if (!check_stopped ())
					throw new Exception ("Target not stopped");
				
				return inferior.CurrentFrame;
			}
		}

		public StackFrame CurrentFrame {
			get {
				if (State != TargetState.CORE_FILE)
					return sse.CurrentFrame;
				else
					return core.CurrentFrame;
			}
		}

		public void Reload ()
		{
			if (StateChanged != null)
				StateChanged (State, 0);

			IMethod method = CurrentMethod;

			if ((method != null) && (MethodChangedEvent != null))
				MethodChangedEvent (method);

			StackFrame frame = CurrentFrame;

			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);

			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		public IMethod CurrentMethod {
			get {
				if (State != TargetState.CORE_FILE)
					return sse.CurrentMethod;
				else
					return core.CurrentMethod;
			}
		}

		public StackFrame[] GetBacktrace ()
		{
			if (State != TargetState.CORE_FILE)
				return sse.GetBacktrace ();
			else
				return core.GetBacktrace ();
		}

		public long GetRegister (int register)
		{
			if (!check_stopped ())
				return 0;
			
			return inferior.GetRegister (register);
		}

		public long[] GetRegisters (int[] registers)
		{
			if (!check_stopped ())
				return null;
			
			return inferior.GetRegisters (registers);
		}

		public void SetRegister (int register, long value)
		{
			if (!check_stopped ())
				return;
			
			inferior.SetRegister (register, value);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			if (!check_stopped ())
				return;
			
			inferior.SetRegisters (registers, values);
		}

		public IDisassembler Disassembler {
			get {
				if (!check_inferior ())
					return null;
				
				return inferior.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				if (!check_inferior ())
					return null;
				
				return inferior.Architecture;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				if (!check_inferior ())
					return null;
				
				return inferior;
			}
		}

		public void UpdateSymbolTable ()
		{
			if (!native)
				csharp_language.UpdateSymbolTable ();
		}

		public Module[] Modules {
			get {
				if (current_modules != null)
					return current_modules;

				return new Module [0];
			}
		}

		public bool BreakpointHit (TargetAddress address)
		{
			if (native)
				return true;

			foreach (ILanguageBackend language in languages) {
				if (!language.BreakpointHit (address))
					return false;
			}

			return true;
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			if (!check_inferior ())
				return null;
			
			return inferior.GetMemoryMaps ();
		}

		[DllImport("glib-2.0")]
		extern static IntPtr g_main_context_default ();

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
					// Do stuff here
					if (symtab_manager != null)
						symtab_manager.Dispose ();
					if (inferior != null)
						inferior.Kill ();
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
