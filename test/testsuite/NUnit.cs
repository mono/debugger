using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using SD = System.Diagnostics;
using ST = System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Core;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	public enum DebuggerEventType {
		TargetEvent,
		ThreadCreated,
		ThreadExited,
		MainProcessCreated,
		ProcessCreated,
		ProcessExecd,
		ProcessExited,
		TargetExited
	}

	public class DebuggerEvent {
		public readonly DebuggerEventType Type;
		public readonly object Data;
		public readonly object Data2;

		public DebuggerEvent (DebuggerEventType type)
			: this (type, null, null)
		{ }

		public DebuggerEvent (DebuggerEventType type, object data)
			: this (type, data, null)
		{ }

		public DebuggerEvent (DebuggerEventType type, object data, object data2)
		{
			this.Type = type;
			this.Data = data;
			this.Data2 = data2;
		}

		public override string ToString ()
		{
			return String.Format ("[{0}:{1}:{2}]", Type, Data, Data2);
		}
	}

	public class StyleNUnit : StyleCLI
	{
		public readonly NUnitInterpreter NUnit;

		public StyleNUnit (NUnitInterpreter nunit)
			: base (nunit)
		{
			this.NUnit = nunit;
		}

		public event TargetEventHandler TargetEventEvent;

		public override void TargetEvent (Thread thread, TargetEventArgs args)
		{
			if (TargetEventEvent != null)
				TargetEventEvent (thread, args);

			base.TargetEvent (thread, args);
		}
	}

	public class NUnitInterpreter : Interpreter
	{
		internal NUnitInterpreter (DebuggerConfiguration config, DebuggerOptions options,
					   LineReader inferior_stdout, LineReader inferior_stderr)
			: base (false, config, options)
		{
			this.inferior_stdout = inferior_stdout;
			this.inferior_stderr = inferior_stderr;

			config.FollowFork = true;

			queue = Queue.Synchronized (new Queue ());
			wait_event = new ST.ManualResetEvent (false);

			Style = style_nunit = new StyleNUnit (this);
			style_nunit.TargetEventEvent += delegate (Thread thread, TargetEventArgs args) {
				AddEvent (new DebuggerEvent (DebuggerEventType.TargetEvent, thread, args));
			};

			ST.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			ObjectFormatter.WrapLines = false;
		}

		Queue queue;
		StyleNUnit style_nunit;
		ST.ManualResetEvent wait_event;
		LineReader inferior_stdout, inferior_stderr;

		public bool HasEvent {
			get { return queue.Count > 0; }
		}

		public DebuggerEvent Wait ()
		{
			for (int i = 0; i < 5; i++) {
				lock (queue.SyncRoot) {
					if (queue.Count > 0)
						return (DebuggerEvent) queue.Dequeue ();

					wait_event.Reset ();
				}

				wait_event.WaitOne (3000, false);
			}

			return null;
		}

		protected void AddEvent (DebuggerEvent e)
		{
			lock (queue.SyncRoot) {
				queue.Enqueue (e);
				wait_event.Set ();
			}
		}

		protected override void OnThreadCreated (Thread thread)
		{
			base.OnThreadCreated (thread);
			AddEvent (new DebuggerEvent (DebuggerEventType.ThreadCreated, thread));
		}

		protected override void OnThreadExited (Thread thread)
		{
			base.OnThreadExited (thread);
			AddEvent (new DebuggerEvent (DebuggerEventType.ThreadExited, thread));
		}

		protected override void OnMainProcessCreated (Process process)
		{
			base.OnMainProcessCreated (process);
			AddEvent (new DebuggerEvent (DebuggerEventType.MainProcessCreated, process));
		}

		protected override void OnProcessCreated (Process process)
		{
			base.OnProcessCreated (process);
			AddEvent (new DebuggerEvent (DebuggerEventType.ProcessCreated, process));
		}

		protected override void OnProcessExited (Process process)
		{
			base.OnProcessExited (process);
			AddEvent (new DebuggerEvent (DebuggerEventType.ProcessExited, process));
		}

		protected override void OnProcessExecd (Process process)
		{
			base.OnProcessExecd (process);
			AddEvent (new DebuggerEvent (DebuggerEventType.ProcessExecd, process));
		}

		protected override void OnTargetExited ()
		{
			base.OnTargetExited ();
			AddEvent (new DebuggerEvent (DebuggerEventType.TargetExited));
		}

		protected override void OnTargetOutput (bool is_stderr, string line)
		{
			if (is_stderr)
				inferior_stderr.Add (line);
			else
				inferior_stdout.Add (line);
		}

		public override void Print (string message)
		{
			if (!IsScript)
				base.Print (message);
		}
	}

	public abstract class TestSuite : MarshalByRefObject
	{
		DebuggerConfiguration config;
		DebuggerOptions options;
		NUnitInterpreter interpreter;
		DebuggerEngine engine;
		LineParser parser;
		LineReader inferior_stdout, inferior_stderr;

		public readonly string ExeFileName;
		public readonly string FileName;

		public static bool Verbose = false;

		Dictionary<string,int> lines;
		Dictionary<string,int> automatic_breakpoints;

		protected static readonly StreamWriter stderr;

		static TestSuite ()
		{
			stderr = new StreamWriter (Console.OpenStandardError ());
			stderr.AutoFlush = true;

			if (Verbose)
				stderr.WriteLine ("PID IS {0}", LibGTop.GetPid ());

			Report.Initialize ();
			Report.ReportWriter.PrintToConsole = false;
		}

		protected TestSuite (string application)
			: this (application + ".exe", application + ".cs")
		{ }

		protected TestSuite (string exe_file, string src_file, params string[] args)
		{
			string srcdir = Path.Combine (BuildInfo.srcdir, "../test/src/");
			string builddir = Path.Combine (BuildInfo.builddir, "../test/src/");

			ExeFileName = Path.GetFullPath (builddir + exe_file);
			FileName = Path.GetFullPath (srcdir + src_file);

			config = new DebuggerConfiguration ();
			options = CreateOptions (ExeFileName, args);

			inferior_stdout = new LineReader ();
			inferior_stderr = new LineReader ();
		}

		const string mdb_line_re = @"//\s+@MDB (LINE|BREAKPOINT):\s+(.*)$";

		protected void ReadSourceFile ()
		{
			lines = new Dictionary<string,int> ();
			automatic_breakpoints = new Dictionary<string,int> ();

			using (StreamReader reader = new StreamReader (FileName)) {
				string text;
				int line = 0;
				while ((text = reader.ReadLine ()) != null) {
					line++;

					Match match = Regex.Match (text, mdb_line_re);
					if (!match.Success)
						continue;

					string name = match.Groups [2].Value;
					lines.Add (name, line);
					if (match.Groups [1].Value == "BREAKPOINT")
						automatic_breakpoints [name] = AssertBreakpoint (line);
				}
			}
		}

		protected void AssertHitBreakpoint (Thread thread, string name, string function)
		{
			AssertHitBreakpoint (thread, automatic_breakpoints [name],
					     function, GetLine (name));
		}

		protected void AssertStopped (Thread thread, string name, string function)
		{
			AssertStopped (thread, function, GetLine (name));
		}

		protected void AssertSegfault (Thread thread, string name, string function)
		{
			AssertSegfault (thread, function, GetLine (name));
		}

		protected int GetBreakpoint (string text)
		{
			return automatic_breakpoints [text];
		}

		protected int GetLine (string text)
		{
			int offset = 0;
			int pos = text.IndexOf ('+');
			if (pos > 0) {
				offset = Int32.Parse (text.Substring (pos + 1));
				text = text.Substring (0, pos);
			}

			if (!lines.ContainsKey (text))
				throw new InternalError ("No such line: {0}", text);
			return lines [text] + offset;
		}

		public NUnitInterpreter Interpreter {
			get { return interpreter; }
		}

		public DebuggerConfiguration Config {
			get { return config; }
		}

		public static string SourceDirectory {
			get {
				string srcdir = Path.Combine (BuildInfo.srcdir, "../test/src/");
				return Path.GetFullPath (srcdir);
			}
		}

		public static string BuildDirectory {
			get {
				string builddir = Path.Combine (BuildInfo.builddir, "../test/src/");
				return Path.GetFullPath (builddir);
			}
		}

		public static string MonoExecutable {
			get {
				return BuildInfo.mono;
			}
		}

		public void Compile ()
		{
			SD.ProcessStartInfo start = new SD.ProcessStartInfo (
				BuildInfo.mcs, "-debug " + FileName + " -out:" + ExeFileName);
			start.UseShellExecute = false;
			start.WorkingDirectory = BuildDirectory;
			start.RedirectStandardOutput = true;
			start.RedirectStandardError = true;

			SD.Process child = SD.Process.Start (start);
			child.WaitForExit ();

			if (child.ExitCode != 0)
				Assert.Fail ("Compilation of {0} exited with error: {1}\n{2}",
					     FileName, child.ExitCode, child.StandardError.ReadToEnd ());
		}

		public static void Debug (string message, params object[] args)
		{
			stderr.WriteLine (message, args);
		}

		[TestFixtureSetUp]
		public virtual void SetUp ()
		{
			interpreter = new NUnitInterpreter (
				config, options, inferior_stdout, inferior_stderr);

			engine = interpreter.DebuggerEngine;
			parser = new LineParser (engine);

			ReadSourceFile ();
		}

		[TestFixtureTearDown]
		public virtual void TearDown ()
		{
			interpreter.Dispose ();
			interpreter = null;
			GC.Collect ();

			if (Verbose) {
				string name = Path.GetFileNameWithoutExtension (FileName);
				stderr.WriteLine ("{0}: {1}", name, PrintMemoryInfo ());
			}
		}

		public string PrintMemoryInfo ()
		{
			int pid = LibGTop.GetPid ();
			int files = LibGTop.GetOpenFiles (pid);
			LibGTop.MemoryInfo info = LibGTop.GetMemoryInfo (pid);
			return String.Format ("size = {0,5}  vsize = {1,7}  resident = {2,5}  " +
					      "share = {3,5}  rss = {4,5}  files = {5,2}",
					      info.Size, info.VirtualSize, info.Resident,
					      info.Share, info.RSS, files);
		}

		private static DebuggerOptions CreateOptions (string application, string[] args)
		{
			DebuggerOptions options = DebuggerOptions.ParseCommandLine (args);

			options.IsScript = true;
			options.File = application;
			options.InferiorArgs = args;

			return options;
		}

		public object AssertExecute (string text)
		{
			parser.Reset ();
			parser.Append (text);
			Command command = parser.GetCommand ();
			if (command == null)
				Assert.Fail ("No such command: `{0}'", text);

			try {
				return command.Execute (engine);
			} catch (ScriptingException ex) {
				Assert.Fail ("Failed to execute command `{0}': {1}.",
					     text, ex.Message);
				return null;
			} catch (TargetException ex) {
				Assert.Fail ("Failed to execute command `{0}': {1}.",
					     text, ex.Message);
				return null;
			} catch (Exception ex) {
				Assert.Fail ("Caught exception while executing command `{0}': {1}",
					     text, ex);
				return null;
			}
		}

		public void AssertExecuteException (string text, string exp_exception)
		{
			parser.Reset ();
			parser.Append (text);
			Command command = parser.GetCommand ();
			if (command == null)
				Assert.Fail ("No such command: `{0}'", text);

			string exception = "";
			try {
				command.Execute (engine);
				Assert.Fail ("Execution of command `{0}' failed to throw " +
					     "exception `{1}'.", command, exp_exception);
			} catch (ScriptingException ex) {
				exception = ex.Message;
			} catch (TargetException ex) {
				exception = ex.Message;
			} catch (Exception ex) {
				Assert.Fail ("Caught exception while executing command `{0}': {1}",
					     text, ex);
			}

			if (exception != exp_exception)
				Assert.Fail ("Execution of command `{0}' threw exception `{1}', " +
					     "but expected `{2}'.", command, exception, exp_exception);
		}

		public void AssertFrame (Thread thread, string line, string function)
		{
			try {
				AssertFrame (thread.CurrentFrame, 0, function, GetLine (line));
			} catch (TargetException ex) {
				Assert.Fail ("Cannot get current frame: {0}.", ex.Message);
			}
		}

		public void AssertFrame (Thread thread, string function, int line)
		{
			try {
				AssertFrame (thread.CurrentFrame, 0, function, line);
			} catch (TargetException ex) {
				Assert.Fail ("Cannot get current frame: {0}.", ex.Message);
			}
		}

		public void AssertFrame (StackFrame frame, int level, string function, int line)
		{
			try {
				Assert.AreEqual (level, frame.Level,
						 "Stack frame is from level {0}, but expected {1}.",
						 frame.Level, level);
				if (frame.SourceLocation == null)
					Assert.Fail ("Current frame `{0}' has no source code.", frame);

				SourceLocation location = frame.SourceLocation;

				string name = location.Name;
				int pos = name.IndexOf (':');
				if (pos > 0)
					name = name.Substring (0, pos);

				Assert.AreEqual (function, name,
						 "Target stopped in method `{0}', but expected `{1}'.",
						 name, function);
				Assert.AreEqual (line, location.Line,
						 "Target stopped at line {0}, but expected {1}.",
						 location.Line, line);
			} catch (TargetException ex) {
				Assert.Fail ("Cannot get current frame: {0}.", ex.Message);
			}
		}

		public void AssertInternalFrame (StackFrame frame, int level)
		{
			Assert.AreEqual (level, frame.Level,
					 "Stack frame is from level {0}, but expected {1}.",
					 level, frame.Level);
			Assert.AreEqual ("<method called from mdb>", frame.Name.ToString (),
					 "Got frame `{0}', but expected a method " +
					 "called from mdb.", frame);
		}

		public void AssertTargetOutput (string line)
		{
			string output = inferior_stdout.ReadLine ();
			if (output == null)
				Assert.Fail ("No target output.");

			Assert.AreEqual (line, output,
					 "Expected target output `{0}', but got `{1}'.",
					 line, output);
		}

		public void AssertNoTargetOutput ()
		{
			if (inferior_stdout.HasEvent) {
				string output = inferior_stdout.ReadLine ();
				Assert.Fail ("Got unexpected target output `{0}'.", output);
			}
		}

		public void AssertStopped (Thread exp_thread, string exp_func, int exp_line)
		{
			TargetEventArgs args = AssertTargetEvent (
				exp_thread, TargetEventType.TargetStopped);

			if ((int) args.Data != 0)
				Assert.Fail ("Received event {0} while waiting for {1} to stop.",
					     args, exp_thread);

			if (exp_func != null)
				AssertFrame (exp_thread, exp_func, exp_line);
		}

		public void AssertSegfault (Thread exp_thread, string exp_func, int exp_line)
		{
			TargetEventArgs args = AssertTargetEvent (
				exp_thread, TargetEventType.TargetStopped);

			if ((int) args.Data != 11)
				Assert.Fail ("Received event {0} while waiting for {1} to stop.",
					     args, exp_thread);

			if (exp_func != null)
				AssertFrame (exp_thread, exp_func, exp_line);
		}

		public void AssertHitBreakpoint (Thread exp_thread, int exp_index,
						 string exp_func, int exp_line)
		{
			TargetEventArgs args = AssertTargetEvent (
				exp_thread, TargetEventType.TargetHitBreakpoint);

			int index = (int) args.Data;
			if ((exp_index != -1) && (index != exp_index))
				Assert.Fail ("Thread {0} hit breakpoint {1}, but expected {2}.",
					     exp_thread, index, exp_index);

			AssertFrame (exp_thread, exp_func, exp_line);
		}

		public void AssertCaughtException (Thread exp_thread, string exp_func, int exp_line)
		{
			AssertTargetEvent (exp_thread, TargetEventType.Exception);
			AssertFrame (exp_thread, exp_func, exp_line);
		}

		public int AssertBreakpoint (int location)
		{
			return AssertBreakpoint (String.Format ("{0}:{1}", FileName, location));
		}

		public int AssertBreakpoint (string location)
		{
			return AssertBreakpoint (false, location);
		}

		public int AssertBreakpoint (bool lazy, string location)
		{
			string command = String.Concat (
				"break ", lazy ? "-lazy " : "", location);
			object result = AssertExecute (command);
			if (result == null) {
				Assert.Fail ("Failed to insert breakpoint.");
				return -1;
			}

			return (int) result;
		}

		public int AssertCatchpoint (string location)
		{
			object result = AssertExecute ("catch " + location);
			if (result == null) {
				Assert.Fail ("Failed to insert catchpoint.");
				return -1;
			}

			return (int) result;
		}

		public Process AssertMainProcessCreated ()
		{
			DebuggerEvent e = AssertEvent (DebuggerEventType.MainProcessCreated);
			return (Process) e.Data;
		}

		public Thread AssertProcessCreated ()
		{
			AssertEvent (DebuggerEventType.ProcessCreated);
			DebuggerEvent ee = AssertEvent (DebuggerEventType.ThreadCreated);
			return (Thread) ee.Data;
		}

		public Thread AssertThreadCreated ()
		{
			DebuggerEvent te = AssertEvent (DebuggerEventType.ThreadCreated);
			return (Thread) te.Data;
		}

		public void AssertProcessExited (Process exp_process)
		{
			DebuggerEvent e = AssertEvent (DebuggerEventType.ProcessExited);
			Process process = (Process) e.Data;

			if (process != exp_process)
				Assert.Fail ("Process {0} exited, but expected process {1} to exit.",
					     process.ID, exp_process.ID);
		}

		public void AssertNoEvent ()
		{
			if (Interpreter.HasEvent) {
				DebuggerEvent e = Interpreter.Wait ();
				Assert.Fail ("Received unexpected event {0}.", e);
			}
		}

		public void AssertTargetExited ()
		{
			while (true) {
				DebuggerEvent e = Interpreter.Wait ();
				if (e == null)
					Assert.Fail ("Time-out while waiting for target to exit.");

				if (e.Type == DebuggerEventType.TargetExited)
					break;
				else if ((e.Type == DebuggerEventType.ThreadExited) ||
					 (e.Type == DebuggerEventType.ProcessExited))
					continue;
				else if (e.Type == DebuggerEventType.TargetEvent) {
					TargetEventArgs args = (TargetEventArgs) e.Data2;
					if ((args.Type == TargetEventType.TargetExited) ||
					    (args.Type == TargetEventType.TargetSignaled))
						continue;
				}

				Assert.Fail ("Received event {0} while waiting for target to exit.", e);
			}

			AssertNoTargetOutput ();
		}

		ScriptingContext GetContext (Thread thread)
		{
			ScriptingContext context = new ScriptingContext (interpreter);
			context.CurrentThread = thread;
			context.CurrentFrame = thread.CurrentFrame;
			return context;
		}

		object EvaluateExpression (ScriptingContext context, string expression)
		{
			try {
				Expression expr = interpreter.ExpressionParser.Parse (expression);
				if (expr == null)
					Assert.Fail ("Cannot parse expression `{0}'.", expression);

				expr = expr.Resolve (context);
				if (expr == null)
					Assert.Fail ("Cannot resolve expression `{0}'.", expression);

				object obj = expr.Evaluate (context);
				if (obj == null)
					Assert.Fail ("Failed to evaluate expression `{0}.", expression);

				return obj;
			} catch (ScriptingException) {
				throw;
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to evalaute expression `{0}': {1}",
					     expression, ex);
				return null;
			}
		}

		TargetType EvaluateExpressionType (ScriptingContext context, string expression)
		{
			try {
				Expression expr = interpreter.ExpressionParser.Parse (expression);
				if (expr == null)
					Assert.Fail ("Cannot parse expression `{0}'.", expression);

				Expression resolved = expr.TryResolveType (context);
				if (resolved != null)
					expr = resolved;
				else
					expr = expr.Resolve (context);
				if (expr == null)
					Assert.Fail ("Cannot resolve expression `{0}'.", expression);

				return expr.EvaluateType (context);
			} catch (ScriptingException) {
				throw;
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to evalaute type of expression `{0}': {1}",
					     expression, ex);
				return null;
			}
		}

		public void AssertPrint (Thread thread, string expression, string exp_result)
		{
			AssertPrint (thread, DisplayFormat.Object, expression, exp_result);
		}

		public void AssertPrint (Thread thread, DisplayFormat format,
					 string expression, string exp_result)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				object obj = EvaluateExpression (context, expression);
				text = context.FormatObject (obj, format);
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to print expression `{0}': {1}",
					     expression, ex);
			}

			if (text != exp_result)
				Assert.Fail ("Expression `{0}' evaluated to `{1}', but expected `{2}'.",
					     expression, text, exp_result);
		}

		public void AssertPrintRegex (Thread thread, DisplayFormat format,
					      string expression, string exp_re)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				object obj = EvaluateExpression (context, expression);
				text = context.FormatObject (obj, format);
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to print expression `{0}': {1}",
					     expression, ex);
			}

			Match match = Regex.Match (text, exp_re);
			if (!match.Success)
				Assert.Fail ("Expression `{0}' evaluated to `{1}', but expected `{2}'.",
					     expression, text, exp_re);
		}

		public void AssertPrintException (Thread thread, string expression, string exp_result)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				object obj = EvaluateExpression (context, expression);
				text = context.FormatObject (obj, DisplayFormat.Object);
				Assert.Fail ("Evaluation of expression `{0}' failed to throw " +
					     "exception {1}.", expression, exp_result);
			} catch (AssertionException) {
				throw;
			} catch (ScriptingException ex) {
				text = ex.Message;
			} catch (Exception ex) {
				Assert.Fail ("Failed to print expression `{0}': {1}",
					     expression, ex);
			}

			if (text != exp_result)
				Assert.Fail ("Expression `{0}' evaluated to `{1}', but expected `{2}'.",
					     expression, text, exp_result);
		}

		public void AssertType (Thread thread, string expression, string exp_result)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				TargetType type = EvaluateExpressionType (context, expression);
				text = context.FormatType (type);
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to evaluate type of expression `{0}': {1}",
					     expression, ex);
			}

			if (text != exp_result)
				Assert.Fail ("Type of expression `{0}' is `{1}', but expected `{2}'.",
					     expression, text, exp_result);
		}

		public void AssertTypeException (Thread thread, string expression, string exp_result)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				TargetType type = EvaluateExpressionType (context, expression);
				text = context.FormatType (type);
				Assert.Fail ("Evaluation of expression `{0}' failed to throw " +
					     "exception {1}.", expression, exp_result);
			} catch (AssertionException) {
				throw;
			} catch (ScriptingException ex) {
				text = ex.Message;
			} catch (Exception ex) {
				Assert.Fail ("Failed to print expression `{0}': {1}",
					     expression, ex);
			}

			if (text != exp_result)
				Assert.Fail ("Expression `{0}' evaluated to `{1}', but expected `{2}'.",
					     expression, text, exp_result);
		}

		public object EvaluateExpression (Thread thread, string expression)
		{
			try {
				ScriptingContext context = GetContext (thread);
				object obj = EvaluateExpression (context, expression);
				if (obj == null)
					Assert.Fail ("Failed to evaluate expression `{0}'", expression);
				return obj;
			} catch (AssertionException) {
				throw;
			} catch (Exception ex) {
				Assert.Fail ("Failed to print expression `{0}': {1}",
					     expression, ex);
				return null;
			}
		}

		public DebuggerEvent AssertEvent ()
		{
			DebuggerEvent e = Interpreter.Wait ();
			if (e == null)
				Assert.Fail ("Time-out while waiting for debugger event.");

			return e;
		}

		public DebuggerEvent AssertEvent (DebuggerEventType type)
		{
			DebuggerEvent e = AssertEvent ();
			if (e.Type != type)
				Assert.Fail ("Received event {0}, but expected {1}.", e, type);

			return e;
		}

		private void AssertMainProcessCreated (Process process)
		{
			DebuggerEvent e = AssertEvent (DebuggerEventType.MainProcessCreated);
			Process main = (Process) e.Data;
			Assert.AreEqual (process, main,
					 "Created main process `{0}', but expected `{1}'.",
					 main, process);
		}

		public Process Start ()
		{
			Process process = Interpreter.Start ();
			AssertMainProcessCreated (process);
			return process;
		}

		public Process LoadSession (Stream stream)
		{
			Process process = Interpreter.LoadSession (stream);
			AssertMainProcessCreated (process);
			return process;
		}

		public Process Attach (int pid)
		{
			Process process = Interpreter.Attach (pid);
			AssertMainProcessCreated (process);
			return process;
		}

		public TargetEventArgs AssertTargetEvent (Thread thread, TargetEventType type)
		{
			DebuggerEvent e = AssertEvent ();
			if (e.Type != DebuggerEventType.TargetEvent)
				Assert.Fail ("Received event {0}, but expected {1}.", e, type);

			if ((thread != null) && (e.Data != thread))
				Assert.Fail ("Received event {0} while waiting for {1} in thread {2}.",
					     e, type, thread);

			TargetEventArgs args = (TargetEventArgs) e.Data2;
			if (args.Type != type)
				Assert.Fail ("Received event {0} while waiting for {1} in thread {2}.",
					     e, type, thread);

			return args;
		}

		public void AssertTargetExited (Process process)
		{
			bool target_event = false;
			bool process_exited = false;
			bool thread_exited = false;

			while (true) {
				DebuggerEvent e = Interpreter.Wait ();
				if (e == null)
					Assert.Fail ("Time-out while waiting for target to exit.");

				if (e.Type == DebuggerEventType.TargetExited)
					break;
				if (e.Type == DebuggerEventType.ThreadExited) {
					if (e.Data == process.MainThread)
						thread_exited = true;
					continue;
				} else if (e.Type == DebuggerEventType.ProcessExited) {
					if (e.Data == process)
						process_exited = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					TargetEventArgs args = (TargetEventArgs) e.Data2;
					if ((args.Type == TargetEventType.TargetExited) ||
					    (args.Type == TargetEventType.TargetSignaled)) {
						if (e.Data == process.MainThread)
							target_event = true;
						continue;
					}
				}

				Assert.Fail ("Received event {0} while waiting for target to exit.", e);
			}

			if (!target_event)
				Assert.Fail ("Did not receive `TargetEventType.TargetExited' event " +
					     "while waiting for target to exit.");
			if (!process_exited)
				Assert.Fail ("Did not receive `ProcessExitedEvent' while waiting for " +
					     "target to exit.");
			if (!thread_exited)
				Assert.Fail ("Did not receive `ThreadExitedEvent' while waiting for " +
					     "target to exit.");

			AssertNoTargetOutput ();
		}

		public void MainLoop ()
		{
			CommandLineInterpreter cmdline = new CommandLineInterpreter (interpreter);
			cmdline.RunInferiorMainLoop ();
		}
	}

	internal class LineReader : MarshalByRefObject
	{
		string current_line = "";
		Queue queue = Queue.Synchronized (new Queue ());

		ST.AutoResetEvent wait_event = new ST.AutoResetEvent (false);

		public void Add (string text)
		{
			lock (queue.SyncRoot) {
			again:
				int pos = text.IndexOf ('\n');
				if (pos < 0)
					current_line += text;
				else {
					current_line += text.Substring (0, pos);
					queue.Enqueue (current_line);
					current_line = "";
					text = text.Substring (pos + 1);
					if (text.Length > 0)
						goto again;
				}

				wait_event.Set ();
			}
		}

		public bool HasEvent {
			get { return queue.Count > 0; }
		}

		public string ReadLine ()
		{
			for (int i = 0; i < 5; i++) {
				lock (queue.SyncRoot) {
					if (queue.Count > 0)
						return (string) queue.Dequeue ();

					wait_event.Reset ();
				}

				wait_event.WaitOne (3000, false);
			}

			return null;
		}
	}
}
