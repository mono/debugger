using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Remoting;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public abstract class Interpreter : MarshalByRefObject
	{
		public readonly ReportWriter ReportWriter;

		DebuggerOptions options;
		DebuggerSession session;
		ScriptingContext context;

		DebuggerClient client;
		Process main_process;
		Thread main_thread;
		Hashtable procs;

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
		ManualResetEvent interrupt_event;

		internal Interpreter (bool is_synchronous, bool is_interactive,
				      DebuggerOptions options)
		{
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;
			this.options = options;

			interrupt_event = new ManualResetEvent (false);

			if (options.HasDebugFlags)
				ReportWriter = new ReportWriter (options.DebugOutput, options.DebugFlags);
			else
				ReportWriter = new ReportWriter ();

			Report.Initialize (ReportWriter);

			procs = new Hashtable ();

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
			try {
				Kill ();
				DebuggerClient.GlobalShutdown ();
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				Environment.Exit (exit_code);
			}
		}

		public ScriptingContext GlobalContext {
			get { return context; }
		}

		public DebuggerSession Session {
			get { return session; }
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
				if (main_thread == null)
					throw new ScriptingException ("No target.");

				return main_thread.TargetMemoryInfo.AddressDomain;
			}
		}

		public Process MainProcess {
			get {
				if (main_process == null)
					throw new ScriptingException ("No target.");

				return main_process;
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

		public Thread[] Threads {
			get {
				Thread[] retval = new Thread [procs.Count];
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
			if (client != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Starting program: {0}",
					   String.Join (" ", options.InferiorArgs));

			try {
				client = DebuggerClient.Run (
					ReportWriter, options.RemoteHost, options.RemoteMono);
				DebuggerServer server = client.DebuggerServer;

				new InterpreterEventSink (this, client, server);
				new ThreadEventSink (this, client, server);

				session = server.Session;

				main_process = server.Run (options);

				start_event.WaitOne ();
				main_thread = main_process.MainThread;
				context.CurrentThread = main_thread;
				Wait (main_thread);

				return main_process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public Process Attach (int pid)
		{
			if (client != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Attaching to {0}", pid);

			try {
				client = DebuggerClient.Run (
					ReportWriter, options.RemoteHost, options.RemoteMono);
				DebuggerServer server = client.DebuggerServer;

				new InterpreterEventSink (this, client, server);
				new ThreadEventSink (this, client, server);

				session = server.Session;

				main_process = server.Attach (options, pid);

				start_event.WaitOne ();
				main_thread = main_process.MainThread;
				context.CurrentThread = main_thread;
				Wait (main_thread);

				return main_process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public Process OpenCoreFile (string core_file)
		{
			if (client != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Loading core file {0}", core_file);

			try {
				client = DebuggerClient.Run (
					ReportWriter, options.RemoteHost, options.RemoteMono);
				DebuggerServer server = client.DebuggerServer;

				new InterpreterEventSink (this, client, server);
				new ThreadEventSink (this, client, server);

				session = server.Session;

				Thread[] threads;
				main_process = server.OpenCoreFile (options, core_file, out threads);
				main_thread = main_process.MainThread;

				context.CurrentThread = main_thread;

				foreach (Thread thread in threads)
					procs.Add (thread.ID, thread);

				return main_process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public void SaveSession (Stream stream)
		{
			BinaryFormatter formatter = new BinaryFormatter ();
			formatter.Serialize (stream, options);
			session.Save (stream);
		}

		public Process LoadSession (Stream stream)
		{
			if (client != null)
				throw new ScriptingException ("Program already started.");

			try {
				BinaryFormatter formatter = new BinaryFormatter ();
				options = (DebuggerOptions) formatter.Deserialize (stream);
				client = DebuggerClient.Run (
					ReportWriter, options.RemoteHost, options.RemoteMono);
				DebuggerServer server = client.DebuggerServer;

				new InterpreterEventSink (this, client, server);
				new ThreadEventSink (this, client, server);

				Process process = server.Run (options);

				session = server.LoadSession (stream);

				session.InsertBreakpoints (main_thread);

				start_event.WaitOne ();
				main_thread = process.MainThread;
				context.CurrentThread = main_thread;
				Wait (main_thread);

				return process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		protected void ThreadCreated (Thread thread)
		{
			procs.Add (thread.ID, thread);

			if (initialized)
				Print ("New thread @{0}", thread.ID);
		}

		public void Wait (Thread thread)
		{
			if (thread == null)
				return;

			WaitHandle[] handles = new WaitHandle [2];
			handles [0] = interrupt_event;
			handles [1] = thread.WaitHandle;

			WaitHandle.WaitAny (handles);
		}

		public void Interrupt ()
		{
			interrupt_event.Set ();
		}

		public void ClearInterrupt ()
		{
			interrupt_event.Reset ();
		}

		protected void DebuggerInitialized ()
		{
			initialized = true;
			start_event.Set ();
		}

		public void ShowBreakpoints ()
		{
			EventHandle[] events = session.Events;
			if (events.Length == 0) {
				Print ("No breakpoints or catchpoints.");
				return;
			}
				       
			Print ("Breakpoints:");
			Print ("{0,3} {1,6} {2,3} {3,12}  {4}", "Id", "Type", "En", "ThreadGroup", "What");
			foreach (EventHandle handle in events) {
				string type;

				if (handle is CatchpointHandle)
					type = "catch";
				else
					type = "break";

				Print ("{0,3} {1,6} {2,3} {3,12}  {4}",
				       handle.Index, type,
				       handle.IsEnabled ? "y" : "n",
				       handle.ThreadGroup != null ? handle.ThreadGroup.Name : "global",
				       handle.Name);
			}
		}

		public EventHandle[] Events {
			get {
				return session.Events;
			}
		}

		public EventHandle GetEvent (int index)
		{
			EventHandle handle = session.GetEvent (index);
			if (handle == null)
				throw new ScriptingException ("No such breakpoint/catchpoint.");

			return handle;
		}

		public void DeleteEvent (Thread thread, EventHandle handle)
		{
			handle.Remove (thread.TargetAccess);
			session.DeleteEvent (handle.Index);
		}

		protected void ThreadExited (Thread thread)
		{
			procs.Remove (thread.ID);
			if (thread == main_thread) {
				TargetExited ();
				context.CurrentThread = null;
			}
		}

		protected void TargetExited ()
		{
			if (client != null) {
				foreach (Thread proc in Threads) {
					if (proc.Process.Debugger == client.DebuggerServer)
						procs.Remove (proc.ID);
				}

				if ((main_thread != null) &&
				    (main_thread.Process.Debugger == client.DebuggerServer)) {
					main_thread = null;
					main_process = null;
					context.CurrentThread = null;
				}
			} else {
				procs = new Hashtable ();
				context.CurrentThread = null;
				main_thread = null;
				main_process = null;
			}

			client = null;
			initialized = false;
		}

		protected void ClientShutdown ()
		{
			Print ("Connection to debugger server terminated.");
			TargetExited ();
		}

		public Thread GetThread (int number)
		{
			if (number == -1)
				return context.CurrentThread;

			foreach (Thread proc in Threads)
				if (proc.ID == number)
					return proc;

			throw new ScriptingException ("No such thread: {0}", number);
		}

		public Thread[] GetThreads (int[] indices)
		{
			Thread[] retval = new Thread [indices.Length];

			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetThread (indices [i]);

			return retval;
		}

		public void ShowThreadGroups ()
		{
			foreach (ThreadGroup group in MainProcess.ThreadGroups) {
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
			if (MainProcess.ThreadGroupExists (name))
				throw new ScriptingException ("A thread group with that name already exists.");

			MainProcess.CreateThreadGroup (name);
		}

		public void DeleteThreadGroup (string name)
		{
			if (!MainProcess.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			MainProcess.DeleteThreadGroup (name);
		}

		public ThreadGroup GetThreadGroup (string name, bool writable)
		{
			if (name == null)
				name = "global";
			if (name.StartsWith ("@"))
				throw new ScriptingException ("No such thread group.");
			if (!MainProcess.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			ThreadGroup group = MainProcess.CreateThreadGroup (name);

			if (writable && group.IsSystem)
				throw new ScriptingException ("Cannot modify system-created thread group.");

			return group;
		}

		public void AddToThreadGroup (string name, Thread[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);

			foreach (Thread thread in threads)
				group.AddThread (thread.ID);
		}

		public void RemoveFromThreadGroup (string name, Thread[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);
	
			foreach (Thread thread in threads)
				group.RemoveThread (thread.ID);
		}

		public int InsertBreakpoint (TargetAccess target, ThreadGroup group, int domain,
					     SourceLocation location)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (location.Name, group);

			EventHandle handle = session.InsertBreakpoint (
				target, domain, location, breakpoint);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			return breakpoint.Index;
		}

		public int InsertBreakpoint (TargetAccess target, ThreadGroup group,
					     TargetFunctionType func)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (func.Name, group);

			EventHandle handle = session.InsertBreakpoint (target, func, breakpoint);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			return breakpoint.Index;
		}

		public int InsertExceptionCatchPoint (TargetAccess target, ThreadGroup group,
						      TargetType exception)
		{
			EventHandle handle = session.InsertExceptionCatchPoint (
				target, group, exception);
			if (handle == null)
				throw new ScriptingException ("Could not add catch point.");

			return handle.Index;
		}

		public void Kill ()
		{
			if (client != null) {
				client.DebuggerServer.Dispose ();
				client.Shutdown ();
				client = null;
			}

			TargetExited ();
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

				backend.ThreadCreatedEvent += thread_created;
				backend.TargetExitedEvent += target_exited;
				backend.InitializedEvent += debugger_initialized;

				client.ClientShutdown += client_shutdown;
			}

			public void thread_created (Debugger debugger, Thread thread)
			{
				thread.TargetOutput += new TargetOutputHandler (target_output);
				interpreter.ThreadCreated (thread);
			}

			public void debugger_initialized (Debugger debugger, Process process)
			{
				interpreter.DebuggerInitialized ();
			}

			public void target_exited ()
			{
				interpreter.TargetExited ();
			}

			public void target_output (bool is_stderr, string line)
			{
				if (is_stderr)
					Report.Error (line);
				else
					Report.Print (line);
			}

			public void client_shutdown (DebuggerClient client)
			{
				interpreter.ClientShutdown ();
			}
		}

		[Serializable]
		protected class ThreadEventSink
		{
			Interpreter interpreter;
			DebuggerClient client;

			public ThreadEventSink (Interpreter interpreter, DebuggerClient client,
						Debugger debugger)
			{
				this.interpreter = interpreter;
				this.client = client;

				debugger.TargetEvent += new TargetEventHandler (target_event);
			}

			public void target_event (Thread thread, TargetAccess target,
						  TargetEventArgs args)
			{
				interpreter.Style.TargetEvent (thread, target, args);
				if ((args.Type == TargetEventType.TargetExited) ||
				    (args.Type == TargetEventType.TargetSignaled))
					interpreter.ThreadExited (thread);
			}
		}
	}
}
