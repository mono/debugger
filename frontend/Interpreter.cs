using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger;
using Mono.Debugger.Languages;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public class Interpreter : DebuggerMarshalByRefObject, IDisposable
	{
		DebuggerConfiguration config;
		DebuggerSession session;
		DebuggerEngine engine;

		Debugger debugger;
		Process main_process;
		Process current_process;

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

		public Interpreter (bool is_synchronous, bool is_interactive,
				    DebuggerConfiguration config, DebuggerOptions options)
		{
			this.config = config;
			this.is_synchronous = is_synchronous;
			this.is_interactive = is_interactive;
			this.session = new DebuggerSession (config, options);
			this.engine = new DebuggerEngine (this);

			interrupt_event = new ManualResetEvent (false);

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
				Dispose ();
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				Environment.Exit (exit_code);
			}
		}

		public StyleBase Style {
			get { return current_style; }
			set {
				current_style = value;
				current_style.Reset ();
			}
		}

		public DebuggerEngine DebuggerEngine {
			get { return engine; }
		}

		public Process CurrentProcess {
			get {
				if (current_process == null)
					throw new TargetException (TargetError.NoTarget);

				return current_process;
			}

			set { current_process = value; }
		}

		public bool HasCurrentProcess {
			get { return current_process != null; }
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
			get { return Options.IsScript; }
		}

		public DebuggerOptions Options {
			get { return session.Options; }
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

		public virtual void Print (string message)
		{
			Report.Print ("{0}\n", message);
		}

		public void Print (string format, params object[] args)
		{
			Print (String.Format (format, args));
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
				return debugger != null;
			}
		}

		public Process Start ()
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			if (!IsScript)
				Print ("Starting program: {0} {1}", Options.File,
				       String.Join (" ", Options.InferiorArgs));

			try {
				debugger = new Debugger (config);

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				current_process = main_process = debugger.Run (session);

				current_thread = current_process.MainThread;
				Wait (current_thread);
				// FIXME: Read the FIXME in DebuggerSession.cs !
				session.MainProcessReachedMain (current_process);

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public Process Attach (int pid)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			if (!IsScript)
				Print ("Attaching to {0}", pid);

			try {
				debugger = new Debugger (config);

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				current_process = main_process = debugger.Attach (session, pid);
				current_thread = current_process.MainThread;
				Wait (current_thread);
				session.MainProcessReachedMain (current_process);

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public Process OpenCoreFile (string core_file)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			Console.WriteLine ("Loading core file {0}", core_file);

			try {
				debugger = new Debugger (config);

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				Thread[] threads;
				current_process = main_process = debugger.OpenCoreFile (
					session, core_file, out threads);

				current_thread = current_process.MainThread;

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public void SaveSession (Stream stream)
		{
			session.SaveSession (stream);
		}

		public Process LoadSession (Stream stream)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			try {
				debugger = new Debugger (config);
				session = new DebuggerSession (config, stream);

				new InterpreterEventSink (this, debugger);
				new ThreadEventSink (this, debugger);

				current_process = main_process = debugger.Run (session);

				current_thread = current_process.MainThread;
				Wait (current_thread);
				session.MainProcessReachedMain (current_process);

				return current_process;
			} catch (TargetException ex) {
				Console.WriteLine ("FUCK: {0}", ex);
				debugger.Dispose ();
				debugger = null;
				throw;
			} catch (Exception ex) {
				Console.WriteLine ("FUCK: {0}", ex);
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public void Wait (CommandResult result)
		{
			if (result == null)
				return;

			WaitHandle[] handles = new WaitHandle [2];
			handles [0] = interrupt_event;
			handles [1] = result.CompletedEvent;

			WaitHandle.WaitAny (handles);

			if (result.Result is Exception)
				throw (Exception) result.Result;
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
			Event[] events = CurrentProcess.Session.Events;
			if (events.Length == 0) {
				Print ("No breakpoints or catchpoints.");
				return;
			}
				       
			Print ("Breakpoints:");
			Print ("{0,3} {1,6} {2,3} {3,12}  {4}", "Id", "Type", "En", "ThreadGroup", "What");
			foreach (Event handle in events) {
				string type;

				if (handle is ExceptionCatchPoint)
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

		public Event GetEvent (int index)
		{
			Event handle = CurrentProcess.Session.GetEvent (index);
			if (handle == null)
				throw new ScriptingException ("No such breakpoint/catchpoint.");

			return handle;
		}

		protected virtual void OnThreadCreated (Thread thread)
		{
			Print ("Process #{0} created new thread @{1}.",
			       thread.Process.ID, thread.ID);
		}

		protected virtual void OnThreadExited (Thread thread)
		{
			if (thread != thread.Process.MainThread)
				Print ("Thread @{0} exited.", thread.ID);
			if (thread == current_thread)
				current_thread = null;
		}

		protected virtual void OnProcessCreated (Process process)
		{
			Print ("Created new process #{0}.", process.ID);
			if (current_process == null) {
				current_process = process;
				current_thread = process.MainThread;
			}
		}

		protected virtual void OnProcessExited (Process process)
		{
			Print ("Process #{0} exited.", process.ID);
			if (process == main_process) {
				current_process = main_process = null;
				current_thread = null;
			} else if (process == current_process) {
				current_process = main_process;
				current_thread = main_process.MainThread;
			}
		}

		protected virtual void OnProcessExecd (Process process)
		{
			Print ("Process #{0} exec()'d: {1}", process.ID,
			       PrintCommandLineArgs (process));
		}

		protected virtual void OnTargetEvent (Thread thread, TargetEventArgs args)
		{
			Style.TargetEvent (thread, args);
		}

		protected virtual void OnTargetOutput (bool is_stderr, string line)
		{
			if (!IsScript) {
				if (is_stderr)
					Report.Error ("{0}", line);
				else
					Report.Print ("{0}", line);
			}
		}

		protected virtual void OnTargetExited ()
		{
			debugger = null;
			main_process = current_process = null;
			current_thread = null;

			Print ("Target exited.");
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new TargetException (TargetError.NoTarget);

				return current_thread;
			}

			set {
				current_thread = value;
				current_process = value.Process;
			}
		}

		public bool HasCurrentThread {
			get { return current_thread != null; }
		}

		public Process GetProcess (int number)
		{
			if (number == -1)
				return CurrentProcess;

			foreach (Process process in Processes) {
				if (process.ID == number)
					return process;
			}

			throw new ScriptingException ("No such process: {0}", number);
		}

		public Thread GetThread (int number)
		{
			if (number == -1)
				return CurrentThread;

			foreach (Process process in Processes) {
				foreach (Thread thread in process.Threads)
					if (thread.ID == number)
						return thread;
			}

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
			foreach (ThreadGroup group in CurrentProcess.Session.ThreadGroups) {
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
			if (CurrentProcess.Session.ThreadGroupExists (name))
				throw new ScriptingException ("A thread group with that name already exists.");

			CurrentProcess.Session.CreateThreadGroup (name);
		}

		public void DeleteThreadGroup (string name)
		{
			if (!CurrentProcess.Session.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			CurrentProcess.Session.DeleteThreadGroup (name);
		}

		public ThreadGroup GetThreadGroup (string name, bool writable)
		{
			if (name == null)
				name = "main";
			else if (name == "global")
				return ThreadGroup.Global;
			if (name.StartsWith ("@"))
				throw new ScriptingException ("No such thread group.");
			if (!CurrentProcess.Session.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			ThreadGroup group = CurrentProcess.Session.CreateThreadGroup (name);

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
			Event handle = target.Process.Session.InsertBreakpoint (
				target, group, domain, location);
			handle.Enable (target);
			return handle.Index;
		}

		public int InsertBreakpoint (Thread target, ThreadGroup group,
					     TargetAddress address)
		{
			Event handle = target.Process.Session.InsertBreakpoint (
				target, group, address);
			handle.Enable (target);
			return handle.Index;
		}

		public int InsertBreakpoint (Thread target, ThreadGroup group,
					     TargetFunctionType func)
		{
			Event handle = target.Process.Session.InsertBreakpoint (target, group, func);
			handle.Enable (target);
			return handle.Index;
		}

		public int InsertExceptionCatchPoint (Thread target, ThreadGroup group,
						      TargetType exception)
		{
			Event handle = target.Process.Session.InsertExceptionCatchPoint (
				target, group, exception);
			handle.Enable (target);
			return handle.Index;
		}

		public int InsertHardwareWatchPoint (Thread target, TargetAddress address)
		{
			Event handle = target.Process.Session.InsertHardwareWatchPoint (
				target, address, BreakpointType.WatchWrite);
			handle.Enable (target);
			return handle.Index;
		}

		public void Kill ()
		{
			if (debugger != null) {
				debugger.Kill ();
				debugger = null;
			}
		}

		public void Kill (Process process)
		{
			if (process == main_process)
				Kill ();
			else
				process.Kill ();
		}

		public void Detach ()
		{
			debugger.Detach ();
		}

		public void Detach (Process process)
		{
			if (process == main_process)
				Detach ();
			else
				process.Detach ();
		}

		public string GetFullPathByFilename (string filename)
		{
			Module[] modules = CurrentProcess.Modules;

			foreach (Module module in modules) {
				if (!module.SymbolsLoaded)
					continue;

				foreach (SourceFile source in module.Sources) {
					if (filename.Equals (source.Name))
						return source.FileName;
				}
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
					Options.WorkingDirectory, DirectorySeparatorStr,
					filename);

			return path;
		}

		public Module[] GetModules (int[] indices)
		{
			int pos = 0;
			Module[] retval = new Module [indices.Length];
			Module[] modules = CurrentProcess.Modules;

			foreach (int index in indices) {
				if ((index < 0) || (index > modules.Length))
					throw new ScriptingException ("No such module {0}.", index);

				retval [pos++] = modules [index];
			}

			return retval;
		}

		public SourceFile[] GetSources (int[] indices)
		{
			Hashtable source_hash = new Hashtable ();
			Module[] modules = CurrentProcess.Modules;

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
					throw new ScriptingException (
						"No such source file: {0}", index);

				retval [pos++] = source;
			}

			return retval;
		}

		public void ModuleOperations (Module[] modules, ModuleOperation[] operations)
		{
			foreach (Module module in modules) {
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
		}

		public Process[] Processes {
			get {
				if (debugger == null)
					throw new TargetException (TargetError.NoTarget);

				return debugger.Processes;
			}
		}

		public string PrintCommandLineArgs (Process process)
		{
			StringBuilder sb = new StringBuilder ();
			string[] args = process.CommandLineArguments;
			int start = 0;
			if ((args.Length > 1) && (args [0] == BuildInfo.mono)) {
				if (args [1] == "--inside-mdb")
					start = 2;
				else
					start = 1;
			}
			for (int i = start; i < args.Length; i++) {
				if (i > start)
					sb.Append (" ");
				sb.Append (args [i]);
			}
			return sb.ToString ();
		}

		public string PrintProcess (Process process)
		{
			string command_line = PrintCommandLineArgs (process);
			if (command_line.Length > 70) {
				command_line = command_line.Substring (0, 70);
				command_line += " ...";
			}
			return String.Format ("#{0} ({1}:{2})", process.ID,
					      process.MainThread.PID, command_line);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (debugger != null) {
					debugger.Kill ();
					debugger = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Interpreter ()
		{
			Dispose (false);
		}

		protected class InterpreterEventSink : DebuggerMarshalByRefObject
		{
			Interpreter interpreter;

			public InterpreterEventSink (Interpreter interpreter, Debugger debugger)
			{
				this.interpreter = interpreter;

				debugger.TargetExitedEvent += target_exited;
				debugger.ThreadCreatedEvent += thread_created;
				debugger.ThreadExitedEvent += thread_exited;
				debugger.ProcessCreatedEvent += process_created;
				debugger.ProcessExitedEvent += process_exited;
				debugger.ProcessExecdEvent += process_execd;
				debugger.TargetOutputEvent += target_output;
			}

			public void thread_created (Debugger debugger, Thread thread)
			{
				interpreter.OnThreadCreated (thread);
			}

			public void thread_exited (Debugger debugger, Thread thread)
			{
				interpreter.OnThreadExited (thread);
			}

			public void process_created (Debugger debugger, Process process)
			{
				interpreter.OnProcessCreated (process);
			}

			public void process_exited (Debugger debugger, Process process)
			{
				interpreter.OnProcessExited (process);
			}

			public void process_execd (Debugger debugger, Process process)
			{
				interpreter.OnProcessExecd (process);
			}

			public void target_exited (Debugger debugger)
			{
				interpreter.OnTargetExited ();
			}

			public void target_output (bool is_stderr, string line)
			{
				interpreter.OnTargetOutput (is_stderr, line);
			}
		}

		[Serializable]
		protected class ThreadEventSink
		{
			Interpreter interpreter;

			public ThreadEventSink (Interpreter interpreter, Debugger debugger)
			{
				this.interpreter = interpreter;

				debugger.TargetEvent += target_event;
			}

			public void target_event (Thread thread, TargetEventArgs args)
			{
				interpreter.OnTargetEvent (thread, args);
			}
		}
	}
}
