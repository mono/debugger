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

	public class DebuggerBackend : ITargetNotification, IDisposable
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
			this.bfd_container = new BfdContainer (this);

			symtab_manager = new SymbolTableManager ();
			symtab_manager.ModulesChangedEvent +=
				new SymbolTableManager.ModuleHandler (modules_reloaded);

			csharp_language = new MonoCSharpLanguageBackend (this);
			csharp_language.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
			bfd_container.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
			languages.Add (csharp_language);
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

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event TargetOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;
		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;
		public event SymbolTableChangedHandler SymbolTableChanged;

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;
		public event ModulesChangedHandler ModulesChangedEvent;

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

			frames_invalid ();	
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
			if (TargetExited != null)
				TargetExited ();
		}

		void inferior_output (string line)
		{
			if (TargetOutput != null)
				TargetOutput (line);
		}

		void inferior_errors (string line)
		{
			if (TargetError != null)
				TargetError (line);
		}

		void debugger_output (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			if (DebuggerError != null)
				DebuggerError (this, message, e);
		}

		public void Run ()
		{
			check_disposed ();
			do_run ((string) null);
		}

		public void ReadCoreFile (string core_file)
		{
			check_disposed ();
			do_run (core_file);
		}

		void do_run (string core_file)
		{
			if (inferior != null)
				throw new AlreadyHaveTargetException ();

			if (target_application == null)
				throw new CannotStartTargetException ("You must specify a program to debug.");

			if (!native) {
				Assembly application;
				try {
					application = Assembly.LoadFrom (target_application);
				} catch (Exception e) {
					application = null;
				}

				if (application != null) {
					// Start it as a CIL application.
					do_run (target_application, core_file, application);
					return;
				}
			}

			// Start it as a native application.
			setup_environment ();

			string[] new_argv = new string [argv.Length + 1];
			new_argv [0] = target_application;
			argv.CopyTo (new_argv, 1);

			native = true;
			if (core_file != null)
				load_core (core_file, new_argv);
			else
				do_run (new_argv);
		}

		void setup_environment ()
		{
			if (argv == null)
				argv = new string [0];
			if (envp == null)
				envp = new string[] { "PATH=" + Environment_Path, "LD_BIND_NOW=yes" };
			if (working_directory == null)
				working_directory = ".";
		}

		void do_run (string target_application, string core_file, Assembly application)
		{
			MethodInfo main = application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			setup_environment ();

			string[] start_argv = {
				Path_Mono, "--break", main_name, "--debug=mono",
				"--noinline", "--nols", "--debug-args", "internal_mono_debugger",
				target_application };

			string[] new_argv = new string [argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			argv.CopyTo (new_argv, start_argv.Length);

			native = false;
			if (core_file != null)
				load_core (core_file, new_argv);
			else
				do_run (new_argv);
		}

		void do_run (string[] argv)
		{
			modules_locked = true;

			if (native)
				load_native_symtab = true;
			inferior = new PTraceInferior (this, working_directory, argv, envp, native,
						       load_native_symtab, bfd_container,
						       new DebuggerErrorHandler (debugger_error));
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.DebuggerOutput += new TargetOutputHandler (debugger_output);
			inferior.DebuggerError += new DebuggerErrorHandler (debugger_error);

			if (!native)
				csharp_language.Inferior = inferior;

			sse = new SingleSteppingEngine (this, inferior, native);
			sse.StateChangedEvent += new StateChangedHandler (target_state_changed);
			sse.MethodInvalidEvent += new MethodInvalidHandler (method_invalid);
			sse.MethodChangedEvent += new MethodChangedHandler (method_changed);
			sse.FrameChangedEvent += new StackFrameHandler (frame_changed);
			sse.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
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

		bool modules_locked = false;

		void modules_changed ()
		{
			if (modules_locked)
				return;

			check_disposed ();
			ArrayList modules = new ArrayList ();
			modules.AddRange (bfd_container.Modules);
			foreach (ILanguageBackend language in languages)
				modules.AddRange (language.Modules);

			symtab_manager.SetModules (modules);
		}

		Module[] current_modules = null;

		void modules_reloaded (object sender, Module[] modules)
		{
			Console.WriteLine ("MODULES RELOADED");
			current_modules = modules;

			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		internal void ReachedMain ()
		{
			modules_locked = true;
			inferior.UpdateModules ();
			UpdateSymbolTable ();
			modules_locked = false;
			modules_changed ();
		}

		internal void ChildExited ()
		{
			if (current_modules == null)
				return;

			modules_locked = true;
			foreach (Module module in current_modules)
				module.UnLoad ();
			modules_locked = false;
			modules_changed ();
		}

		void load_core (string core_file, string[] argv)
		{
			core = new CoreFileElfI386 (this, argv [0], core_file, bfd_container);
			inferior = core;

			if (!native)
				csharp_language.Inferior = inferior;

			Reload ();
		}

		public void Quit ()
		{
			if (inferior != null)
				inferior.Shutdown ();
		}

		void check_inferior ()
		{
			check_disposed ();
			if (inferior == null)
				throw new NoTargetException ();
		}

		void check_stopped ()
		{
			check_inferior ();

			if ((State != TargetState.STOPPED) && (State != TargetState.CORE_FILE))
				throw new TargetNotStoppedException ();
		}

		void check_can_run ()
		{
			check_inferior ();

			if (sse == null)
				throw new CannotExecuteCoreFileException ();

			if (State == TargetState.CORE_FILE)
				throw new CannotExecuteCoreFileException ();
			else if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();
		}

		public void StepInstruction ()
		{
			check_can_run ();
			sse.StepInstruction ();
		}

		public void NextInstruction ()
		{
			check_can_run ();
			sse.NextInstruction ();
		}

		public void StepLine ()
		{
			check_can_run ();
			sse.StepLine ();
		}

		public void NextLine ()
		{
			check_can_run ();
			sse.NextLine ();
		}

		public void Continue ()
		{
			check_can_run ();
			sse.Continue ();
		}

		public void Continue (TargetAddress until)
		{
			check_can_run ();
			TargetAddress current = CurrentFrameAddress;

			Console.WriteLine (String.Format ("Requested to run from {0:x} until {1:x}.",
							  current, until));

			while (current < until)
				current += inferior.Disassembler.GetInstructionSize (current);

			if (current != until)
				Console.WriteLine (String.Format (
					"Oooops: reached {0:x} but symfile had {1:x}",
					current, until));

			sse.Continue (until);
		}

		public void Stop ()
		{
			check_inferior ();
			inferior.Stop ();
		}

		public void ClearSignal ()
		{
			check_inferior ();
			inferior.SetSignal (0);
		}

		public void Finish ()
		{
			check_can_run ();
			sse.Finish ();
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
			Console.WriteLine ("BREAKPOINT INSERTED: {0}", index);
			return index;
		}

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
			Console.WriteLine ("BREAKPOINT INSERTED: {0}", index);
			return index;
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_stopped ();
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
			check_stopped ();
			return inferior.GetRegister (register);
		}

		public long[] GetRegisters (int[] registers)
		{
			check_stopped ();
			return inferior.GetRegisters (registers);
		}

		public void SetRegister (int register, long value)
		{
			check_stopped ();
			inferior.SetRegister (register, value);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			check_stopped ();
			inferior.SetRegisters (registers, values);
		}

		public IDisassembler Disassembler {
			get {
				check_inferior ();
				return inferior.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_inferior ();
				return inferior.Architecture;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_inferior ();
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
			check_inferior ();
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
