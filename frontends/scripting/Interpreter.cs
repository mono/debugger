using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

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
		UserInterface current_user_interface;

		DebuggerTextWriter command_output;
		DebuggerTextWriter inferior_output;
		bool is_synchronous;
		bool is_interactive;
		int exit_code = 0;

		internal static readonly string DirectorySeparatorStr;

		static Interpreter ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		internal Interpreter (DebuggerTextWriter command_out,
				      DebuggerTextWriter inferior_out,
				      bool is_synchronous, bool is_interactive,
				      string[] args)
		{
			this.command_output = command_out;
			this.inferior_output = inferior_out;
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;

			procs = new Hashtable ();

			breakpoints = new Hashtable ();

			user_interfaces = new Hashtable ();
			user_interfaces.Add ("mono", new UserInterfaceMono (this));
			user_interfaces.Add ("native", new UserInterfaceNative (this));
			user_interfaces.Add ("martin", new UserInterfaceMartin (this));
			current_user_interface = (UserInterface) user_interfaces ["mono"];

			context = new ScriptingContext (this, is_interactive, true);

			start = ProcessStart.Create (null, args);
			if (start != null)
				Initialize ();

			if (!HasBackend)
				return;

			try {
				Run ();
			} catch (TargetException e) {
				Console.WriteLine ("Cannot start target: {0}", e.Message);
			}

			context.CurrentProcess = current_process;
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

		public UserInterface UI {
			get { return current_user_interface; }
			set {
				current_user_interface = value;
				current_user_interface.Reset ();
			}
		}

		public UserInterface GetUserInterface (string name)
		{
			UserInterface ui = (UserInterface) user_interfaces [name];
			if (ui == null)
				throw new ScriptingException (
					"No such user interface: `{0}'", name);

			return ui;
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

		public ProcessStart Start (DebuggerOptions options, string[] args)
		{
			if (backend != null)
				throw new ScriptingException ("Already have a target.");

			start = ProcessStart.Create (options, args);

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

			return process;
		}

		void thread_created (ThreadManager manager, Process process)
		{
			ProcessHandle handle = new ProcessHandle (this, process);
			add_process (handle);
		}

		void modules_changed ()
		{
			modules = backend.Modules;
		}

		public void ShowBreakpoints ()
		{
			Print ("Breakpoints:");
			foreach (BreakpointHandle handle in breakpoints.Values) {
				Print ("{0} ({1}): {3} {2}", handle.Breakpoint.Index,
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

				foreach (SourceFile source in module.Sources)
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

				if (!module.HasDebuggingInfo)
					continue;

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

			foreach (SourceFile source in module.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		void process_exited (ProcessHandle process)
		{
			procs.Remove (process.ID);
			if (process == current_process)
				current_process = null;
		}

		void add_process (ProcessHandle process)
		{
			process.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
			procs.Add (process.Process.ID, process);
		}

		void target_exited ()
		{
			if (backend != null)
				backend.Dispose ();
			backend = null;
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
			SourceLocation location = backend.FindMethod (name);

			if (location != null)
				return location;
			else
				throw new ScriptingException ("No such method.");
		}

		public ISourceBuffer FindFile (string filename)
		{
			return backend.SourceFileFactory.FindFile (filename);
		}

		public void Kill ()
		{
			if (backend != null)
				backend.ThreadManager.Kill ();
		}
	}
}
