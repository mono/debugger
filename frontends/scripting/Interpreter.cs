using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;

using Mono.GetOptions;

namespace Mono.Debugger.Frontends.Scripting
{
	[AttributeUsage (AttributeTargets.Class)]
	public class ShortDescriptionAttribute : Attribute
	{
		string text;

		public ShortDescriptionAttribute (string text)
		{
			this.text = text;
		}

		public string Text {
			get { return text; }
		}
	}

	[AttributeUsage (AttributeTargets.Class)]
	public class HelpAttribute : Attribute
	{
		string text;

		public HelpAttribute (string text)
		{
			this.text = text;
		}

		public string Text {
			get { return text; }
		}
	}

	public abstract class Interpreter
	{
		DebuggerBackend backend;
		Module[] modules;
		ProcessStart start;

		ScriptingContext context;

		ProcessHandle current_process;
		Hashtable procs;
		Hashtable breakpoints;
		Hashtable user_interfaces;
		Style current_user_interface;

		DebuggerTextWriter command_output;
		DebuggerTextWriter inferior_output;
		bool is_synchronous;
		bool is_interactive;
		bool is_script;
		bool initialized;
		int exit_code = 0;

		AutoResetEvent start_event;

		internal static readonly string DirectorySeparatorStr;

		static Interpreter ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		internal Interpreter (DebuggerTextWriter command_out,
				      DebuggerTextWriter inferior_out,
				      bool is_synchronous, bool is_interactive,
				      DebuggerOptions options)
		{
			this.command_output = command_out;
			this.inferior_output = inferior_out;
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;
			this.is_script = options.IsScript;

			procs = new Hashtable ();
			breakpoints = new Hashtable ();

			user_interfaces = new Hashtable ();
			user_interfaces.Add ("mono", new StyleMono (this));
			user_interfaces.Add ("native", new StyleNative (this));
			user_interfaces.Add ("martin", new StyleMartin (this));
			current_user_interface = (Style) user_interfaces ["mono"];

			start_event = new AutoResetEvent (false);

			context = new ScriptingContext (this, is_interactive, true);

			start = ProcessStart.Create (options);
			if (start != null)
				Initialize ();

			if (!HasBackend)
				return;

			try {
				Run ();
			} catch (TargetException e) {
				Error (e);
			}
		}

		public DebuggerBackend Initialize ()
		{
			if (backend != null)
				return backend;
			if (start == null)
				throw new ScriptingException ("No program loaded.");

			backend = new DebuggerBackend ();
			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
			backend.ThreadManager.TargetOutputEvent += new TargetOutputHandler (target_output);
			backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
			backend.ThreadManager.TargetExitedEvent += new TargetExitedHandler (target_exited);
			backend.ThreadManager.InitializedEvent += new ThreadEventHandler (thread_manager_initialized);
			return backend;
		}

		public void Exit ()
		{
			if (backend != null)
				backend.Dispose ();

			Environment.Exit (exit_code);
		}

		public ScriptingContext GlobalContext {
			get { return context; }
		}

		public ProcessStart ProcessStart {
			get { return start; }
		}

		public Style Style {
			get { return current_user_interface; }
			set {
				current_user_interface = value;
				current_user_interface.Reset ();
			}
		}

		public Style GetStyle (string name)
		{
			Style style = (Style) user_interfaces [name];
			if (style == null)
				throw new ScriptingException (
					"No such user interface: `{0}'", name);

			return style;
		}

		public DebuggerBackend DebuggerBackend {
			get {
				if (backend != null)
					return backend;

				throw new ScriptingException ("No backend loaded.");
			}
		}

		public bool IsSynchronous {
			get { return is_synchronous; }
		}

		public bool IsInteractive {
			get { return is_interactive; }
		}

		public bool IsScript {
			get { return is_script; }
		}

		public int ExitCode {
			get { return exit_code; }
			set { exit_code = value; }
		}

		public ProcessHandle[] Processes {
			get {
				ProcessHandle[] retval = new ProcessHandle [procs.Count];
				procs.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		public ProcessHandle CurrentProcess {
			get {
				if (current_process == null)
					throw new ScriptingException ("No target.");

				return current_process;
			}

			set {
				current_process = value;
			}
		}

		public string GetFullPath (string filename)
		{
			if (start == null)
				return Path.GetFullPath (filename);

			if (Path.IsPathRooted (filename))
				return filename;

			return String.Concat (start.BaseDirectory, DirectorySeparatorStr, filename);
		}

		void target_output (bool is_stderr, string line)
		{
			PrintInferior (is_stderr, line);
		}

		public void Abort ()
		{
			Print ("Caught fatal error while running non-interactively; exiting!");
			Environment.Exit (-1);
		}

		public void Error (string message)
		{
			if (IsScript)
				command_output.WriteLine (true, "ERROR: " + message);
			else
				command_output.WriteLine (true, message);
			if (!IsInteractive)
				Abort ();
		}

		public void Error (string format, params object[] args)
		{
			Error (String.Format (format, args));
		}

		public void Error (ScriptingException ex)
		{
			Error (ex.Message);
		}

		public void Error (TargetException ex)
		{
			Error (ex.Message);
		}

		public void Print (string message)
		{
			command_output.WriteLine (false, message);
		}

		public void Print (string format, params object[] args)
		{
			Print (String.Format (format, args));
		}

		public void Print (object obj)
		{
			Print (obj.ToString ());
		}

		public void PrintInferior (bool is_stderr, string line)
		{
			inferior_output.Write (is_stderr, line);
		}

		public bool HasBackend {
			get {
				return backend != null;
			}
		}

		public bool HasTarget {
			get {
				return current_process != null;
			}
		}

		public ProcessStart Start (DebuggerOptions options)
		{
			if (backend != null)
				throw new ScriptingException ("Already have a target.");

			start = ProcessStart.Create (options);

			return start;
		}

		public Process Run ()
		{
			if (current_process != null)
				throw new ScriptingException ("Process already started.");
			if (backend == null)
				throw new ScriptingException ("No program loaded.");

			backend.Run (start);
			Process process = backend.ThreadManager.WaitForApplication ();
			current_process = (ProcessHandle) procs [process.ID];

			start_event.WaitOne ();
			context.CurrentProcess = current_process;
			return process;
		}

		public Process Run (ProcessStart start)
		{
			if (backend != null)
				throw new ScriptingException ("Already have a target.");

			this.start = start;
			Initialize ();

			try {
				return Run ();
			} catch (TargetException e) {
				throw new ScriptingException (
					"Cannot start target: {0}", e.Message);
			}
		}

		void thread_created (ThreadManager manager, Process process)
		{
			ProcessHandle handle = new ProcessHandle (this, process);
			handle.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
			procs.Add (handle.Process.ID, handle);

			if (initialized)
				Print ("New process @{0}", process.ID);
		}

		void thread_manager_initialized (ThreadManager manager, Process process)
		{
			initialized = true;
			start_event.Set ();
		}

		void modules_changed ()
		{
			modules = backend.Modules;
		}

		public void ShowBreakpoints ()
		{
			Print ("Breakpoints:");
			foreach (BreakpointHandle handle in breakpoints.Values) {
				Print ("{0} ({1}): [{3}] {2}", handle.Breakpoint.Index,
				       handle.Breakpoint.ThreadGroup.Name, handle.Breakpoint,
				       handle.IsEnabled ? "*" : " ");
			}
		}

		public BreakpointHandle GetBreakpoint (int index)
		{
			BreakpointHandle handle = (BreakpointHandle) breakpoints [index];
			if (handle == null)
				throw new ScriptingException ("No such breakpoint.");

			return handle;
		}

		public void DeleteBreakpoint (ProcessHandle process, BreakpointHandle handle)
		{
			handle.RemoveBreakpoint (process.Process);
			breakpoints.Remove (handle.Breakpoint.Index);
		}

		public Module[] GetModules (int[] indices)
		{
			if (modules == null)
				throw new ScriptingException ("No modules.");

			backend.ModuleManager.Lock ();

			int pos = 0;
			Module[] retval = new Module [indices.Length];

			foreach (int index in indices) {
				if ((index < 0) || (index > modules.Length))
					throw new ScriptingException ("No such module {0}.", index);

				retval [pos++] = modules [index];
			}

			backend.ModuleManager.UnLock ();

			return retval;
		}

		public SourceFile[] GetSources (int[] indices)
		{
			if (modules == null)
				throw new ScriptingException ("No modules.");

			Hashtable source_hash = new Hashtable ();

			backend.ModuleManager.Lock ();

			foreach (Module module in modules) {
				if (!module.SymbolsLoaded)
					continue;

				foreach (SourceFile source in module.SymbolFile.Sources)
					source_hash.Add (source.ID, source);
			}

			int pos = 0;
			SourceFile[] retval = new SourceFile [indices.Length];

			foreach (int index in indices) {
				SourceFile source = (SourceFile) source_hash [index];
				if (source == null)
					throw new ScriptingException ("No such source file: {0}", index);

				retval [pos++] = source;
			}

			backend.ModuleManager.UnLock ();

			return retval;
		}

		public void ShowModules ()
		{
			if (modules == null) {
				Print ("No modules.");
				return;
			}

			for (int i = 0; i < modules.Length; i++) {
				Module module = modules [i];

				Print ("{0,4} {1}{2}{3}{4}{5}", i, module.Name,
				       module.IsLoaded ? " loaded" : "",
				       module.SymbolsLoaded ? " symbols" : "",
				       module.StepInto ? " step" : "",
				       module.LoadSymbols ? "" :  " ignore");
			}
		}

		void module_operation (Module module, ModuleOperation[] operations)
		{
			foreach (ModuleOperation operation in operations) {
				switch (operation) {
				case ModuleOperation.Ignore:
					module.LoadSymbols = false;
					break;
				case ModuleOperation.UnIgnore:
					module.LoadSymbols = true;
					break;
				case ModuleOperation.Step:
					module.StepInto = true;
					break;
				case ModuleOperation.DontStep:
					module.StepInto = false;
					break;
				default:
					throw new InternalError ();
				}
			}
		}

		public void ModuleOperations (Module[] modules, ModuleOperation[] operations)
		{
			backend.ModuleManager.Lock ();

			foreach (Module module in modules)
				module_operation (module, operations);

			backend.ModuleManager.UnLock ();
			backend.SymbolTableManager.Wait ();
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.SymbolFile.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		void process_exited (ProcessHandle process)
		{
			procs.Remove (process.ID);
			if (process == current_process)
				current_process = null;
		}

		void target_exited ()
		{
			if (backend != null)
				backend.Dispose ();
			backend = null;

			current_process = null;
			procs = new Hashtable ();
			breakpoints = new Hashtable ();

			context = new ScriptingContext (this, is_interactive, true);
		}

		public ProcessHandle GetProcess (int number)
		{
			if (number == -1)
				return CurrentProcess;

			foreach (ProcessHandle proc in Processes)
				if (proc.ID == number)
					return proc;

			throw new ScriptingException ("No such process: {0}", number);
		}

		public ProcessHandle[] GetProcesses (int[] indices)
		{
			ProcessHandle[] retval = new ProcessHandle [indices.Length];

			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetProcess (indices [i]);

			return retval;
		}

		public void ShowThreadGroups ()
		{
			foreach (ThreadGroup group in ThreadGroup.ThreadGroups) {
				if (group.Name.StartsWith ("@"))
					continue;
				StringBuilder ids = new StringBuilder ();
				foreach (int thread in group.Threads) {
					ids.Append (" @");
					ids.Append (thread);
				}
				Print ("{0}:{1}", group.Name, ids.ToString ());
			}
		}

		public void CreateThreadGroup (string name)
		{
			if (ThreadGroup.ThreadGroupExists (name))
				throw new ScriptingException ("A thread group with that name already exists.");

			ThreadGroup.CreateThreadGroup (name);
		}

		public void DeleteThreadGroup (string name)
		{
			if (!ThreadGroup.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			ThreadGroup.DeleteThreadGroup (name);
		}

		public ThreadGroup GetThreadGroup (string name, bool writable)
		{
			if (name == null)
				name = "global";
			if (name.StartsWith ("@"))
				throw new ScriptingException ("No such thread group.");
			if (!ThreadGroup.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			ThreadGroup group = ThreadGroup.CreateThreadGroup (name);

			if (writable && group.IsSystem)
				throw new ScriptingException ("Cannot modify system-created thread group.");

			return group;
		}

		public void AddToThreadGroup (string name, ProcessHandle[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);

			foreach (ProcessHandle process in threads)
				group.AddThread (process.Process.ID);
		}

		public void RemoveFromThreadGroup (string name, ProcessHandle[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);
	
			foreach (ProcessHandle process in threads)
				group.RemoveThread (process.Process.ID);
		}

		public int InsertBreakpoint (ProcessHandle thread, ThreadGroup group,
					     SourceLocation location)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (location.Name, group);

			BreakpointHandle handle = location.InsertBreakpoint (
				thread.Process, breakpoint);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			breakpoints.Add (breakpoint.Index, handle);

			return breakpoint.Index;
		}

		public SourceLocation FindLocation (string file, int line)
		{
			string path = GetFullPath (file);
			SourceLocation location = backend.FindLocation (path, line);

			if (location != null)
				return location;
			else
				throw new ScriptingException ("No method contains the specified file/line.");
		}

		public SourceLocation FindMethod (string name)
		{
			return backend.FindMethod (name);
		}

		public ISourceBuffer FindFile (string filename)
		{
			return backend.SourceFileFactory.FindFile (filename);
		}

		public void Kill ()
		{
			target_exited ();
		}

		public void LoadLibrary (Process process, string filename)
		{
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				backend.LoadLibrary (process, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}

		public void SaveSession (Stream stream)
		{
			Session session = new Session ();

			session.Modules = DebuggerBackend.Modules;

			ArrayList list = new ArrayList ();
			foreach (BreakpointHandle handle in breakpoints.Values) {
				if (handle.SourceLocation == null) {
					Print ("Warning: Cannot save breakpoint {0}",
					       handle.Breakpoint.Index);
					continue;
				}

				list.Add (handle);
			}

			session.Breakpoints = new BreakpointHandle [list.Count];
			list.CopyTo (session.Breakpoints, 0);

			session.Save (this, stream);
		}

		public void LoadSession (Stream stream)
		{
			Session session = Session.Load (this, stream);
			foreach (BreakpointHandle handle in session.Breakpoints)
				breakpoints.Add (handle.Breakpoint.Index, handle);
		}

		public void Restart ()
		{
			using (MemoryStream stream = new MemoryStream ()) {
				SaveSession (stream);
				stream.Seek (0, SeekOrigin.Begin);
				Kill ();
				LoadSession (stream);
			}
		}
	}
}
