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

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public abstract class Interpreter : MarshalByRefObject
	{
		public readonly ReportWriter ReportWriter;

		DebuggerOptions options;
		DebuggerSession session;

		Debugger debugger;
		Process main_process;
		Thread main_thread;

		Hashtable styles;
		StyleBase current_style;

		Hashtable parsers_by_name;
		Hashtable parser_names_by_language;
		string current_parser_name;

		bool is_synchronous;
		bool is_interactive;
		int exit_code = 0;

		ManualResetEvent interrupt_event;
		Thread current_thread;

		internal static readonly string DirectorySeparatorStr;

		static Interpreter ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

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
		}

		AppDomain debugger_domain;
		static int domain_age = 0;

		public void Exit ()
		{
			try {
				Kill ();
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				Environment.Exit (exit_code);
			}
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
	    
			string result = Report.ReadLine ();
			return (result == "y") || (result == "yes");
		}

		public void PrintInstruction (AssemblerLine line)
		{
			if (line.Label != null)
				Print ("{0}:", line.Label);
			Print ("{0:11x}\t{1}", line.Address, line.Text);
		}

		public TargetFunctionType QueryMethod (TargetFunctionType[] methods)
		{
			Report.Print ("More than one method matches your query:\n");

			ArrayList list = new ArrayList ();

			foreach (TargetFunctionType method in methods) {
				if (method.Source == null)
					continue;

				list.Add (method);
				Report.Print ("{0,4}  {1}\n", list.Count, method.Name);
			}

			Report.Print ("Select a method or 0 to abort: ");
			string result = Report.ReadLine ();

			uint index;
			try {
				index = UInt32.Parse (result);
			} catch {
				Report.Print ("Invalid number.");
				return null;
			}

			if (index == 0)
				return null;

			if (index > list.Count) {
				Report.Print ("No such method.");
				return null;
			}

			return (TargetFunctionType) list [(int) index];
		}

		public bool HasTarget {
			get {
				return main_process != null;
			}
		}

		public Process Start ()
		{
			if (debugger != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Starting program: {0}",
					   String.Join (" ", options.InferiorArgs));

			try {
				debugger = new Debugger ();

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				session = debugger.Session;

				main_process = debugger.Run (options);

				main_thread = main_process.MainThread;
				CurrentThread = main_thread;
				Wait (main_thread);

				return main_process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public Process Attach (int pid)
		{
			if (debugger != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Attaching to {0}", pid);

			try {
				debugger = new Debugger ();

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				session = debugger.Session;

				main_process = debugger.Attach (options, pid);

				main_thread = main_process.MainThread;
				CurrentThread = main_thread;
				Wait (main_thread);

				return main_process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		public Process OpenCoreFile (string core_file)
		{
			if (debugger != null)
				throw new ScriptingException ("Program already started.");

			Console.WriteLine ("Loading core file {0}", core_file);

			try {
				debugger = new Debugger ();

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				session = debugger.Session;

				Thread[] threads;
				main_process = debugger.OpenCoreFile (options, core_file, out threads);
				main_thread = main_process.MainThread;

				CurrentThread = main_thread;

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
			if (debugger != null)
				throw new ScriptingException ("Program already started.");

			try {
				BinaryFormatter formatter = new BinaryFormatter ();
				options = (DebuggerOptions) formatter.Deserialize (stream);
				debugger = new Debugger ();

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				Process process = debugger.Run (options);

				session = DebuggerSession.Load (debugger, stream);

				session.InsertBreakpoints (main_thread);

				main_thread = process.MainThread;
				CurrentThread = main_thread;
				Wait (main_thread);

				return process;
			} catch (TargetException e) {
				Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		protected void ThreadCreated (Thread thread)
		{
			if (!thread.IsDaemon)
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
			handle.Remove (thread);
			session.DeleteEvent (handle.Index);
		}

		protected void ThreadExited (Thread thread)
		{
			if (thread == main_thread) {
				TargetExited ();
				CurrentThread = null;
			}
		}

		protected void TargetExited ()
		{
			if (main_process != null) {
				if ((main_thread != null) &&
				    (main_thread.Process == main_process)) {
					main_thread = null;
					main_process = null;
					CurrentThread = null;
				}
			} else {
				CurrentThread = null;
				main_thread = null;
				main_process = null;
			}
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new ScriptingException ("No program to debug.");

				return current_thread;
			}

			set { current_thread = value; }
		}

		public Thread GetThread (int number)
		{
			if (number == -1)
				return CurrentThread;

			foreach (Thread proc in MainProcess.Threads)
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

		public int InsertBreakpoint (Thread target, ThreadGroup group, int domain,
					     SourceLocation location)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (location.Name, group);

			EventHandle handle = session.InsertBreakpoint (
				target, domain, location, breakpoint);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			return breakpoint.Index;
		}

		public int InsertBreakpoint (Thread target, ThreadGroup group,
					     TargetFunctionType func)
		{
			Breakpoint breakpoint = new SimpleBreakpoint (func.Name, group);

			EventHandle handle = session.InsertBreakpoint (target, func, breakpoint);
			if (handle == null)
				throw new ScriptingException ("Could not insert breakpoint.");

			return breakpoint.Index;
		}

		public int InsertExceptionCatchPoint (Thread target, ThreadGroup group,
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
			if (debugger != null) {
				debugger.Dispose ();
				debugger = null;
			}
			TargetExited ();
		}

		public string GetFullPathByFilename (string filename)
		{
			try {
				main_process.ModuleManager.Lock ();

				Module[] modules = main_process.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.Sources) {
						if (filename.Equals (source.Name))
							return source.FileName;
					}
				}
			} finally {
				main_process.ModuleManager.UnLock ();
			}

			return null;
		}

		public string GetFullPath (string filename)
		{
			if (Path.IsPathRooted (filename))
				return filename;

			string path = GetFullPathByFilename (filename);
			if (path == null)
				path = String.Concat (
					options.WorkingDirectory, DirectorySeparatorStr,
					filename);

			return path;
		}

		public Process[] Processes {
			get {
				if (debugger == null)
					throw new ScriptingException ("No target.");

				return debugger.Processes;
			}
		}

		protected class InterpreterEventSink : MarshalByRefObject
		{
			Interpreter interpreter;

			public InterpreterEventSink (Interpreter interpreter, Debugger backend)
			{
				this.interpreter = interpreter;

				backend.ThreadCreatedEvent += thread_created;
				backend.ProcessReachedMainEvent += process_reached_main;
				backend.ProcessExitedEvent += process_exited;
			}

			public void thread_created (Debugger debugger, Thread thread)
			{
				thread.TargetOutput += new TargetOutputHandler (target_output);
				interpreter.ThreadCreated (thread);
			}

			public void process_reached_main (Debugger debugger, Process process)
			{
			}

			public void process_exited (Debugger debugger, Process process)
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
		}

		[Serializable]
		protected class ThreadEventSink
		{
			Interpreter interpreter;

			public ThreadEventSink (Interpreter interpreter, Debugger debugger)
			{
				this.interpreter = interpreter;

				debugger.TargetEvent += new TargetEventHandler (target_event);
			}

			public void target_event (Thread thread, TargetEventArgs args)
			{
				interpreter.Style.TargetEvent (thread, args);
				if ((args.Type == TargetEventType.TargetExited) ||
				    (args.Type == TargetEventType.TargetSignaled))
					interpreter.ThreadExited (thread);
			}
		}
	}
}
