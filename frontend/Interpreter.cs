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
		protected readonly DebuggerManager manager;
		protected readonly DebuggerOptions options;

		ScriptingContext context;

		Process main_process;
		Hashtable procs;
		Hashtable events;

		Hashtable styles;
		StyleBase current_style;

		Hashtable parsers_by_name;
		Hashtable parser_names_by_language;
		string current_parser_name;

		bool is_synchronous;
		bool is_interactive;
		bool initialized;
		int exit_code = 0;

		AutoResetEvent start_event;

		internal Interpreter (bool is_synchronous, bool is_interactive,
				      DebuggerOptions options)
		{
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;
			this.options = options;

			manager = new DebuggerManager (options);

			procs = new Hashtable ();
			events = new Hashtable ();

			styles = new Hashtable ();
			styles.Add ("cli", new StyleCLI (this));
			styles.Add ("emacs", new StyleEmacs (this));
			current_style = (StyleBase) styles ["cli"];

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

		public void Exit ()
		{
			Kill ();
			DebuggerClient.GlobalShutdown ();
			Environment.Exit (exit_code);
		}

		public DebuggerManager DebuggerManager {
			get { return manager; }
		}

		public ScriptingContext GlobalContext {
			get { return context; }
		}

		public StyleBase Style {
			get { return current_style; }
			set {
				current_style = value;
				current_style.Reset ();
			}
		}

		public AddressDomain AddressDomain {
			get {
				if (main_process == null)
					throw new ScriptingException ("No target.");

				return main_process.TargetMemoryInfo.AddressDomain;
			}
		}

		public StyleBase GetStyle (string name)
		{
			StyleBase style = (StyleBase) styles [name];
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
						string l = (string)parser_names_by_language [context.CurrentLanguage.Name];
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
				parser_type = (Type)parsers_by_name [parser_names_by_language [context.CurrentLanguage.Name]];
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

		public Process[] Processes {
			get {
				Process[] retval = new Process [procs.Count];
				procs.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		public void Abort ()
		{
			Print ("Caught fatal error while running non-interactively; exiting!");
			Environment.Exit (-1);
		}

		public void Error (string message)
		{
			if (IsScript)
				Report.Error ("ERROR: {0}\n", message);
			else
				Report.Error ("ERROR: {0}\n", message);
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
			Report.Print ("{0}\n", message);
		}

		public void Print (string format, params object[] args)
		{
			Report.Print (format, args);
			Report.Print ("\n");
		}

		public void Print (object obj)
		{
			Print (obj.ToString ());
		}

		public bool Query (string prompt) {

			Report.Print (prompt);
			Report.Print (" (y or n) ");
	    
			int c = Console.Read ();
			Console.Read (); /* consume the \n */
			return (c == 'y');
		}

		public bool HasTarget {
			get {
				return main_process != null;
			}
		}

		public Process Start ()
		{
			if (main_process != null)
				throw new ScriptingException ("Process already started.");

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
				DebuggerClient client;
				client = manager.Run (options.RemoteHost, options.RemoteMono);
				Debugger backend = client.DebuggerServer;

				new InterpreterEventSink (this, client, backend);
				new ProcessEventSink (this, client, backend);

				backend.Run (options, argv);
				Process process = backend.WaitForApplication ();
				main_process = process;

				start_event.WaitOne ();
				context.CurrentProcess = main_process;
				manager.Wait (process);

				return process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public Process StartServer (string[] argv)
		{
			try {
				DebuggerClient client = manager.Run (null, null);
				Debugger backend = client.DebuggerServer;

				new InterpreterEventSink (this, client, backend);
				new ProcessEventSink (this, client, backend);

				backend.Run (null, argv);
				Process process = backend.WaitForApplication ();
				Print ("Server started: @{0}", process.ID);
				process.Continue (true);
				process.Wait ();

				return process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		protected void ThreadCreated (Process process)
		{
			procs.Add (process.ID, process);

			if (initialized)
				Print ("New process @{0}", process.ID);
		}

		protected void DebuggerInitialized ()
		{
			initialized = true;
			start_event.Set ();
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

		public void DeleteEvent (Process process, IEventHandle handle)
		{
			handle.Remove (process.TargetAccess);
			events.Remove (handle.Breakpoint.Index);
		}

		protected void ProcessExited (DebuggerClient client, Process process)
		{
			procs.Remove (process.ID);
			if (process == main_process) {
				TargetExited (client);
				context.CurrentProcess = null;
			}
		}

		protected void TargetExited (DebuggerClient client)
		{
			if (client != null) {
				manager.TargetExited (client);

				foreach (Process proc in Processes) {
					if (proc.Debugger == client.DebuggerServer)
						procs.Remove (proc.ID);
				}

				if ((main_process != null) &&
				    (main_process.Debugger == client.DebuggerServer)) {
					main_process = null;
					context.CurrentProcess = null;
				}
			} else {
				procs = new Hashtable ();
				context.CurrentProcess = null;
				main_process = null;
			}

			events = new Hashtable ();
			initialized = false;
		}

		public Process GetProcess (int number)
		{
			if (number == -1)
				return context.CurrentProcess;

			foreach (Process proc in Processes)
				if (proc.ID == number)
					return proc;

			throw new ScriptingException ("No such process: {0}", number);
		}

		public Process[] GetProcesses (int[] indices)
		{
			Process[] retval = new Process [indices.Length];

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

		public void AddToThreadGroup (string name, Process[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);

			foreach (Process process in threads)
				group.AddThread (process.ID);
		}

		public void RemoveFromThreadGroup (string name, Process[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);
	
			foreach (Process process in threads)
				group.RemoveThread (process.ID);
		}

		public int InsertBreakpoint (Process thread, ThreadGroup group,
					     SourceLocation location)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (location.Name, group);

			EventHandle handle = thread.Debugger.InsertBreakpoint (
				thread.TargetAccess, breakpoint, location);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			events.Add (breakpoint.Index, handle);

			return breakpoint.Index;
		}

		public int InsertBreakpoint (Process thread, ThreadGroup group,
					     TargetFunctionType func)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (func.Name, group);

			EventHandle handle = thread.Debugger.InsertBreakpoint (
				thread.TargetAccess, breakpoint, func);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			events.Add (breakpoint.Index, handle);

			return breakpoint.Index;
		}

		public int InsertExceptionCatchPoint (Language language, Process thread,
						      ThreadGroup group, TargetType exception)
		{
			Breakpoint breakpoint = new ExceptionCatchPoint (language, exception, group);

			CatchpointHandle handle = CatchpointHandle.Create (
				thread.TargetAccess, breakpoint);

			if (handle == null)
				throw new ScriptingException ("Could not add catch point.");

			events.Add (breakpoint.Index, handle);

			return breakpoint.Index;
		}

		public void Kill ()
		{
			manager.Kill ();
			TargetExited (null);
		}

		protected class InterpreterEventSink : MarshalByRefObject
		{
			Interpreter interpreter;
			DebuggerClient client;

			public InterpreterEventSink (Interpreter interpreter, DebuggerClient client,
						     Debugger backend)
			{
				this.interpreter = interpreter;
				this.client = client;

				backend.ThreadCreatedEvent += new DebuggerEventHandler (thread_created);
				backend.TargetExitedEvent += new TargetExitedHandler (target_exited);
				backend.InitializedEvent += new DebuggerEventHandler (debugger_initialized);
			}

			public void thread_created (Debugger debugger, Process process)
			{
				process.TargetOutput += new TargetOutputHandler (target_output);
				interpreter.ThreadCreated (process);
			}

			public void debugger_initialized (Debugger debugger, Process process)
			{
				interpreter.DebuggerInitialized ();
			}

			public void target_exited ()
			{
				interpreter.TargetExited (client);
			}

			public void target_output (bool is_stderr, string line)
			{
				if (is_stderr)
					Report.Error (line);
				else
					Report.Print (line);
			}
		}

		[Serializable]
		protected class ProcessEventSink
		{
			Interpreter interpreter;
			DebuggerClient client;

			public ProcessEventSink (Interpreter interpreter, DebuggerClient client,
						 Debugger debugger)
			{
				this.interpreter = interpreter;
				this.client = client;

				debugger.TargetEvent += new TargetEventHandler (target_event);
			}

			public void target_event (TargetAccess target, TargetEventArgs args)
			{
				Process process = (Process) interpreter.procs [target.ID];
				interpreter.Style.TargetEvent (target, process, args);
				if ((args.Type == TargetEventType.TargetExited) ||
				    (args.Type == TargetEventType.TargetSignaled))
					interpreter.ProcessExited (client, process);
			}
		}
	}
}
