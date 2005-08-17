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
using Mono.Debugger.Remoting;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public abstract class Interpreter : MarshalByRefObject
	{
		InterpreterEventSink event_sink;
		DebuggerClient client;
		DebuggerBackend backend;
		Module[] modules;
		DebuggerOptions options;

		ScriptingContext context;

		ProcessHandle current_process;
		Hashtable procs;
		Hashtable events;

		Hashtable styles;
		Style current_style;

		Hashtable parsers_by_name;
		Hashtable parser_names_by_language;
		string current_parser_name;

		DebuggerTextWriter command_output;
		DebuggerTextWriter inferior_output;
		bool is_synchronous;
		bool is_interactive;
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
			this.options = options;

			procs = new Hashtable ();
			events = new Hashtable ();

			styles = new Hashtable ();
			styles.Add ("mono", new StyleMono (this));
			styles.Add ("native", new StyleNative (this));
			styles.Add ("martin", new StyleMartin (this));
			styles.Add ("emacs", new StyleEmacs (this));
			current_style = (Style) styles ["mono"];

			parsers_by_name = new Hashtable ();
			parsers_by_name.Add ("c#", typeof (CSharp.ExpressionParser));

			// XXX we should really get these from the
			// actual Name property of a language
			// instance..
			parser_names_by_language = new Hashtable ();
			parser_names_by_language.Add ("Mono", "c#");
			parser_names_by_language.Add ("native", "c#");

			current_parser_name = "auto";

			start_event = new AutoResetEvent (false);

			context = new ScriptingContext (this, is_interactive, true);
		}

		AppDomain debugger_domain;
		static int domain_age = 0;

		public DebuggerBackend Initialize ()
		{
			if (backend != null)
				return backend;

			if (options.IsRemote)
				client = new DebuggerClient (options.RemoteHost, options.RemoteMono);
			else
				client = new DebuggerClient ();
			backend = client.DebuggerBackend;

			event_sink = new InterpreterEventSink (this, backend);
			return backend;
		}

		public void Exit ()
		{
			Kill ();
			DebuggerClient.GlobalShutdown ();
			Environment.Exit (exit_code);
		}

		public ScriptingContext GlobalContext {
			get { return context; }
		}

		public Style Style {
			get { return current_style; }
			set {
				current_style = value;
				current_style.Reset ();
			}
		}

		public Style GetStyle (string name)
		{
			Style style = (Style) styles [name];
			if (style == null)
				throw new ScriptingException (
					"No such user interface: `{0}'", name);

			return style;
		}

		public string[] GetStyleNames ()
		{
			string[] names = new string[styles.Keys.Count];
			styles.Keys.CopyTo (names, 0);

			return names;
		}

		public string CurrentLang {
			get { return current_parser_name; }
			set {
				if (value == "auto" || parsers_by_name [value] != null) {
					current_parser_name = value;
				}
				else {
					throw new ScriptingException ("No such language: `{0}'", value);
				}
			}
		}

		public string CurrentLangPretty {
			get {
				if (current_parser_name == "auto") {
					try {
						string l = (string)parser_names_by_language [context.CurrentFrame.Frame.Language.Name];
						return String.Format ("auto, currently set to {0}", l);
					}
					catch { }
				}

				return current_parser_name;
			}
		}

		public IExpressionParser GetExpressionParser (ScriptingContext context, string name)
		{
			Type parser_type;

			if (current_parser_name == "auto") {
				/* determine the language parser by the current stack frame */
				parser_type = (Type)parsers_by_name [parser_names_by_language [context.CurrentFrame.Frame.Language.Name]];
			}
			else {
				/* use the user specified language */
				parser_type = (Type)parsers_by_name [current_parser_name];
			}

			if (parser_type != null) {
				IExpressionParser parser;
				object[] args = new object[2];
				args[0] = context;
				args[1] = name;
				parser = (IExpressionParser)Activator.CreateInstance (parser_type, args);

				return parser;
			}
			else {
				return new CSharp.ExpressionParser (context, name);
			}
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
			get { return options.IsScript; }
		}

		public DebuggerOptions Options {
			get { return options; }
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

		public string GetFullPathByFilename (string filename){
			string retval = null;

			backend.ModuleManager.Lock ();

			foreach (Module module in modules) {
				if (!module.SymbolsLoaded)
					continue;

				foreach (SourceFile source in module.SymbolFile.Sources) {
					if (filename.Equals (source.Name)) {
						retval = source.FileName;
						break;
					}
				}
			}

			backend.ModuleManager.UnLock ();

			return retval;
		}


		public string GetFullPath (string filename)
		{
			if (backend == null)
				return Path.GetFullPath (filename);

			if (Path.IsPathRooted (filename))
				return filename;

			string path = GetFullPathByFilename (filename);
			if (path == null)
				path = String.Concat (
					backend.ProcessStart.BaseDirectory, DirectorySeparatorStr,
					filename);

			return path;
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

		public bool Query (string prompt) {

			command_output.Write (false, prompt);
			command_output.Write (false, " (y or n) ");
	    
			int c = Console.Read ();
			Console.Read (); /* consume the \n */
			return (c == 'y');
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

		public Process Start ()
		{
			string[] argv;

			if (options.InferiorArgs == null)
				argv = new string [1];
			else {
				argv = new string [options.InferiorArgs.Length + 1];
				options.InferiorArgs.CopyTo (argv, 1);
			}

			argv[0] = options.File;

			Console.WriteLine ("Starting program: {0}", String.Join (" ", argv));

			try {
				Initialize ();
				return Run (argv);
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		protected Process Run (string[] argv)
		{
			if (current_process != null)
				throw new ScriptingException ("Process already started.");
			if (backend == null)
				throw new ScriptingException ("No program loaded.");

			backend.Run (options, argv);
			Process process = backend.ThreadManager.WaitForApplication ();
			current_process = (ProcessHandle) procs [process.ID];

			start_event.WaitOne ();
			context.CurrentProcess = current_process;

			event_sink.AddTargetOutput (process);

			return process;
		}

		protected void ThreadCreated (ProcessHandle handle)
		{
			procs.Add (handle.Process.ID, handle);

			if (initialized)
				Print ("New process @{0}", handle.Process.ID);
		}

		protected void ThreadManagerInitialized (ThreadManager manager, Process process)
		{
			initialized = true;
			start_event.Set ();
		}

		protected void ModulesChanged ()
		{
			modules = backend.Modules;
		}

		public void ShowBreakpoints ()
		{
			if (events.Values.Count == 0) {
				Print ("No breakpoints or catchpoints.");
				return;
			}
				       
			Print ("Breakpoints:");
			Print ("{0,3} {1,6} {2,3} {3,12}  {4}", "Id", "Type", "En", "ThreadGroup", "What");
			foreach (IEventHandle handle in events.Values) {
			  string type;

			  if (handle is CatchpointHandle)
			    type = "catch";
			  else
			    type = "break";

				Print ("{0,3} {1,6} {2,3} {3,12}  {4}",
				       handle.Breakpoint.Index,
				       type,
				       handle.IsEnabled ? "y" : "n",
				       handle.Breakpoint.ThreadGroup.Name, handle.Breakpoint.Name);
			}
		}

		public IEventHandle[] Events {
			get {
				IEventHandle[] ret = new IEventHandle [events.Values.Count];
				events.Values.CopyTo (ret, 0);
				return ret;
			}
		}

		public IEventHandle GetEvent (int index)
		{
			IEventHandle handle = (IEventHandle) events [index];

			if (handle == null)
				throw new ScriptingException ("No such breakpoint/catchpoint.");

			return handle;
		}

		public void DeleteEvent (ProcessHandle process, IEventHandle handle)
		{
			handle.Remove (process.Process);
			events.Remove (handle.Breakpoint.Index);
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

		public Module[] Modules
		{
			get {
				if (modules == null)
					throw new ScriptingException ("No modules.");

				backend.ModuleManager.Lock ();

				Module[] retval = new Module [modules.Length];

				Array.Copy (modules, retval, modules.Length);

				backend.ModuleManager.UnLock ();

				return retval;
			}
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

			Print ("{0,4} {1,5} {2,5} {3}", "Id", "step?", "sym?", "Name");
			for (int i = 0; i < modules.Length; i++) {
				Module module = modules [i];

				Print ("{0,4} {1,5} {2,5} {3}",
				       i,
				       module.StepInto ? "y " : "n ",
				       module.SymbolsLoaded ? "y " : "n ",
				       module.Name);
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

		protected void ProcessExited (ProcessHandle process)
		{
			procs.Remove (process.ID);
			if (process == current_process)
				current_process = null;
		}

		protected void TargetExited ()
		{
			if (client != null) {
				client.Shutdown ();
				client = null;
			}

			backend = null;
			event_sink = null;

			current_process = null;
			procs = new Hashtable ();
			events = new Hashtable ();

			context = new ScriptingContext (this, is_interactive, true);

			initialized = false;
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

			events.Add (breakpoint.Index, handle);

			return breakpoint.Index;
		}

		public int InsertExceptionCatchPoint (ILanguage language, ProcessHandle thread, ThreadGroup group,
						      ITargetType exception)
		{
			Breakpoint breakpoint = new ExceptionCatchPoint (language, exception, group);

			CatchpointHandle handle = CatchpointHandle.Create (
				thread.Process, breakpoint);

			if (handle == null)
				throw new ScriptingException ("Could not add catch point.");

			events.Add (breakpoint.Index, handle);

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

		public SourceLocation FindLocation (SourceLocation location, int line)
		{
			if (location.HasSourceFile)
				return FindLocation (location.SourceFile.FileName, line);

			if (line > location.SourceBuffer.Contents.Length)
				throw new ScriptingException ("Requested line is outside the current buffer.");

			return new SourceLocation (location.Module, location.SourceBuffer, line);
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
			if (backend != null) {
				backend.Dispose ();
				backend = null;
			}

			TargetExited ();
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

		protected class InterpreterEventSink : MarshalByRefObject
		{
			Interpreter interpreter;

			public InterpreterEventSink (Interpreter interpreter, DebuggerBackend backend)
			{
				this.interpreter = interpreter;

				backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
				backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
				backend.ThreadManager.TargetExitedEvent += new TargetExitedHandler (target_exited);
				backend.ThreadManager.InitializedEvent += new ThreadEventHandler (thread_manager_initialized);
			}

			public void AddTargetOutput (Process process)
			{
				process.TargetOutput += new TargetOutputHandler (target_output);
			}

			public void thread_created (ThreadManager manager, Process process)
			{
				ProcessHandle handle = new ProcessHandle (interpreter, process);
				handle.ProcessExitedEvent += new ProcessExitedHandler (process_exited);
				handle.Process.TargetOutput += new TargetOutputHandler (target_output);
				interpreter.ThreadCreated (handle);
			}

			public void thread_manager_initialized (ThreadManager manager, Process process)
			{
				interpreter.ThreadManagerInitialized (manager, process);
			}

			public void modules_changed ()
			{
				interpreter.ModulesChanged ();
			}

			public void target_exited ()
			{
				interpreter.TargetExited ();
			}

			public void process_exited (ProcessHandle process)
			{
				interpreter.ProcessExited (process);
			}

			public void target_output (bool is_stderr, string line)
			{
				interpreter.PrintInferior (is_stderr, line);
			}
		}
	}
}
