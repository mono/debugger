using System;
using System.IO;
using System.Collections;
using ST = System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	public abstract class TestSuite
	{
		DebuggerOptions options;
		Interpreter interpreter;
		DebuggerEngine engine;
		LineParser parser;
		LineReader debugger_output;
		LineReader inferior_stdout, inferior_stderr;

		public readonly string ExeFileName;
		public readonly string FileName;

		static Regex breakpoint_regex = new Regex (@"^Breakpoint ([0-9]+) at ([^\n\r]+)$");
		static Regex catchpoint_regex = new Regex (@"^Inserted catch point ([0-9]+) for ([^\n\r]+)$");
		static Regex stopped_regex = new Regex (@"^Thread @([0-9]+) stopped at ([^\n\r]+)\.$");
		static Regex hit_breakpoint_regex = new Regex (@"^Thread @([0-9]+) hit breakpoint ([0-9]+) at ([^\n\r]+)\.$");
		static Regex caught_exception_regex = new Regex (@"^Thread @([0-9]+) caught exception at ([^\n\r]+)\.$");
		static Regex frame_regex = new Regex (@"^#([0-9]+): 0x[0-9A-Fa-f]+ in (.*)$");
		static Regex func_source_regex = new Regex (@"^(.*) at (.*):([0-9]+)$");
		static Regex func_offset_regex = new Regex (@"^(.*)\+0x([0-9A-Fa-f]+)$");
		static Regex process_created_regex = new Regex (@"^Created new process #([0-9]+)\.$");
		static Regex thread_created_regex = new Regex (@"^Process #([0-9]+) created new thread @([0-9]+)\.$");
		static Regex process_execd_regex = new Regex (@"^Process #([0-9]+) exec\(\)'d\: (.*)$");
		static Regex process_exited_regex = new Regex (@"^Process #([0-9]+) exited\.$");


		protected TestSuite (string application)
			: this (application + ".exe", application + ".cs")
		{ }

		protected TestSuite (string exe_file, string src_file, params string[] args)
		{
			string srcdir = Path.Combine (BuildInfo.srcdir, "../test/src/");
			string builddir = Path.Combine (BuildInfo.builddir, "../test/src/");

			ExeFileName = Path.GetFullPath (builddir + exe_file);
			FileName = Path.GetFullPath (srcdir + src_file);

			options = CreateOptions (ExeFileName, args);

			debugger_output = new LineReader ();
			inferior_stdout = new LineReader ();
			inferior_stderr = new LineReader ();
		}

		public Interpreter Interpreter {
			get { return interpreter; }
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

		[TestFixtureSetUp]
		public virtual void SetUp ()
		{
			interpreter = new Interpreter (true, true, options);
			interpreter.TargetOutputEvent += delegate (bool is_stderr, string line) {
				if (is_stderr)
					inferior_stderr.Add (line);
				else
					inferior_stdout.Add (line);
			};

			interpreter.DebuggerOutputEvent += delegate (string text) {
				debugger_output.Add (text);
			};

			engine = new DebuggerEngine (interpreter);
			parser = new LineParser (engine);
		}

		[TestFixtureTearDown]
		public virtual void TearDown ()
		{
			interpreter.Dispose ();
			interpreter = null;
			GC.Collect ();
		}

		private static DebuggerOptions CreateOptions (string application, string[] args)
		{
			DebuggerOptions options = new DebuggerOptions ();

			options.IsScript = true;
			options.File = application;
			options.InferiorArgs = new string [args.Length + 1];
			options.InferiorArgs [0] = options.File;
			args.CopyTo (options.InferiorArgs, 1);

			return options;
		}

		public void AssertExecute (string text)
		{
			parser.Reset ();
			parser.Append (text);
			Command command = parser.GetCommand ();
			if (command == null)
				Assert.Fail ("No such command: `{0}'", text);

			try {
				command.Execute (engine);
			} catch (ScriptingException ex) {
				Assert.Fail ("Failed to execute command `{0}': {1}.",
					     text, ex.Message);
			} catch (TargetException ex) {
				Assert.Fail ("Failed to execute command `{0}': {1}.",
					     text, ex.Message);
			} catch (Exception ex) {
				Assert.Fail ("Caught exception while executing command `{0}': {1}",
					     text, ex);
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
						 level, frame.Level);
				if (frame.SourceAddress == null)
					Assert.Fail ("Current frame `{0}' has no source code.", frame);
				SourceLocation location = frame.SourceAddress.Location;
				Assert.AreEqual (function, location.Method.Name,
						 "Target stopped in method `{0}', but expected `{1}'.",
						 function, location.Method.Name);
				Assert.AreEqual (line, location.Line,
						 "Target stopped at line {0}, but expected {1}.",
						 location.Name, line);
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
			inferior_stdout.Wait ();
			string output = inferior_stdout.ReadLine ();
			if (output == null)
				Assert.Fail ("No target output.");

			Assert.AreEqual (line, output,
					 "Expected target output `{0}', but got `{1}'.",
					 line, output);
		}

		public void AssertNoTargetOutput ()
		{
			string output;
			while ((output = inferior_stdout.ReadLine ()) != null)
				Assert.Fail ("Got unexpected target output `{0}'.", output);
		}

		public void AssertDebuggerOutput (string line)
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("No debugger output.");

			Assert.AreEqual (line, output,
					 "Expected debugger output `{0}', but got `{1}'.",
					 line, output);
		}

		public void AssertNoDebuggerOutput ()
		{
			string output;
			while ((output = debugger_output.ReadLine ()) != null)
				Assert.Fail ("Got unexpected debugger output `{0}'.", output);
		}

		public void AssertFrame (string frame, int exp_index, string exp_func, int exp_line)
		{
			Match match = frame_regex.Match (frame);
			if (!match.Success)
				Assert.Fail ("Received unknown stack frame `{0}'.", frame);

			int index = Int32.Parse (match.Groups [1].Value);
			string func = match.Groups [2].Value;

			Match source_match = func_source_regex.Match (func);
			if (!source_match.Success)
				Assert.Fail ("Received unknown stack frame `{0}'.", frame);

			func = source_match.Groups [1].Value;
			string file = source_match.Groups [2].Value;
			int line = Int32.Parse (source_match.Groups [3].Value);

			Match offset_match = func_offset_regex.Match (func);
			if (offset_match != null)
				func = offset_match.Groups [1].Value;

			if (index != exp_index)
				Assert.Fail ("Received frame {0}, but expected {1}.", index, exp_index);

			if (file != FileName)
				Assert.Fail ("Target stopped in {0}, but expected {1}.",
					     file, FileName);

			if (func != exp_func)
				Assert.Fail ("Target stopped in function `{0}', but expected `{1}'.",
					     func, exp_func);

			if (line != exp_line)
				Assert.Fail ("Target stopped in line {0}, but expected {1}.",
					     line, exp_line);
		}

		public void AssertStopped (Thread exp_thread, string exp_func, int exp_line)
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Target not stopped.");

			Match match = stopped_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Target not stopped (received `{0}').", output);

			int thread = Int32.Parse (match.Groups [1].Value);
			string frame = match.Groups [2].Value;

			if (thread != exp_thread.ID)
				Assert.Fail ("Thread {0} stopped at {1}, but expected thread {2} to stop.",
					     thread, frame, exp_thread.ID);

			if (exp_func != null) {
				AssertFrame (exp_thread, exp_func, exp_line);
				AssertFrame (frame, 0, exp_func, exp_line);
			}
		}

		public void AssertHitBreakpoint (Thread exp_thread, int exp_index,
						 string exp_func, int exp_line)
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Target not stopped.");

			Match match = hit_breakpoint_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Target not stopped.");

			int thread = Int32.Parse (match.Groups [1].Value);
			int index = Int32.Parse (match.Groups [2].Value);
			string frame = match.Groups [3].Value;

			if (thread != exp_thread.ID)
				Assert.Fail ("Thread {0} stopped at {1}, but expected thread {2} to stop.",
					     thread, frame, exp_thread.ID);

			if ((exp_index != -1) && (index != exp_index))
				Assert.Fail ("Thread {0} hit breakpoint {1}, but expected {2}.",
					     thread, index, exp_index);

			AssertFrame (exp_thread, exp_func, exp_line);
			AssertFrame (frame, 0, exp_func, exp_line);
		}

		public void AssertCaughtException (Thread exp_thread, string exp_func, int exp_line)
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Target not stopped.");

			Match match = caught_exception_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Target not stopped.");

			int thread = Int32.Parse (match.Groups [1].Value);
			string frame = match.Groups [2].Value;

			if (thread != exp_thread.ID)
				Assert.Fail ("Thread {0} stopped at {1}, but expected thread {2} to stop.",
					     thread, frame, exp_thread.ID);

			AssertFrame (exp_thread, exp_func, exp_line);
			AssertFrame (frame, 0, exp_func, exp_line);
		}

		public int AssertBreakpoint (int location)
		{
			return AssertBreakpoint (location.ToString ());
		}

		public int AssertBreakpoint (string location)
		{
			AssertNoDebuggerOutput ();
			AssertExecute ("break " + location);

			string output = debugger_output.ReadLine ();
			if (output == null) {
				Assert.Fail ("Failed to insert breakpoint.");
				return -1;
			}

			Match match = breakpoint_regex.Match (output);
			if (!match.Success) {
				Assert.Fail ("Failed to insert breakpoint.");
				return -1;
			}

			return Int32.Parse (match.Groups [1].Value);
		}

		public int AssertCatchpoint (string location)
		{
			AssertNoDebuggerOutput ();
			AssertExecute ("catch " + location);

			string output = debugger_output.ReadLine ();
			if (output == null) {
				Assert.Fail ("Failed to insert catchpoint.");
				return -1;
			}

			Match match = catchpoint_regex.Match (output);
			if (!match.Success) {
				Assert.Fail ("Failed to insert catchpoint.");
				return -1;
			}

			return Int32.Parse (match.Groups [1].Value);
		}

		public Thread AssertProcessCreated ()
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Failed to created process (received `{0}').", output);

			Match match = process_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Failed to created process (received `{0}').", output);

			debugger_output.Wait ();
			output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Failed to created process (received `{0}').", output);

			match = thread_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Failed to created process (received `{0}').", output);

			int id = Int32.Parse (match.Groups [2].Value);
			return interpreter.GetThread (id);
		}

		public void AssertProcessExited (Process process)
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exit (received `{0}').", output);

			Match match = process_exited_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exit (received `{0}').", output);

			int id = Int32.Parse (match.Groups [1].Value);
			Assert.AreEqual (id, process.ID,
					 "Process {0} exited, but expected process {1} to exit.",
					 id, process.ID);
		}

		public Thread AssertProcessExecd ()
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			Match match = process_execd_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			debugger_output.Wait ();
			output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			match = thread_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			int id = Int32.Parse (match.Groups [2].Value);
			return interpreter.GetThread (id);
		}

		public Thread AssertProcessForkedAndExecd ()
		{
			debugger_output.Wait ();
			string output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			Match match = process_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			debugger_output.Wait ();
			output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			match = thread_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			debugger_output.Wait ();
			output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			match = process_execd_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			debugger_output.Wait ();
			output = debugger_output.ReadLine ();
			if (output == null)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			match = thread_created_regex.Match (output);
			if (!match.Success)
				Assert.Fail ("Process failed to exec() (received `{0}').", output);

			int id = Int32.Parse (match.Groups [2].Value);
			return interpreter.GetThread (id);
		}

		public void AssertTargetExited ()
		{
			AssertDebuggerOutput ("Target exited.");
			AssertNoDebuggerOutput ();
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
				IExpressionParser parser = interpreter.GetExpressionParser (
					context, expression);

				Expression expr = parser.Parse (expression);
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
				IExpressionParser parser = interpreter.GetExpressionParser (
					context, expression);

				Expression expr = parser.Parse (expression);
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

		public void AssertPrintException (Thread thread, string expression, string exp_result)
		{
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				object obj = EvaluateExpression (context, expression);
				text = context.FormatObject (obj, DisplayFormat.Object);
				Assert.Fail ("Evaluation of exception `{0}' failed to throw " +
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

		private class LineReader
		{
			string current_line = "";
			Queue lines = new Queue ();

			bool waiting;
			ST.AutoResetEvent wait_event = new ST.AutoResetEvent (false);

			public void Add (string text)
			{
				lock (this) {
				again:
					int pos = text.IndexOf ('\n');
					if (pos < 0)
						current_line += text;
					else {
						current_line += text.Substring (0, pos);
						lines.Enqueue (current_line);
						current_line = "";
						text = text.Substring (pos + 1);
						if (text.Length > 0)
							goto again;
					}

					if (!waiting)
						return;
				}

				waiting = false;
				wait_event.Set ();
			}

			public void Wait ()
			{
				lock (this) {
					if (lines.Count > 0)
						return;

					waiting = true;
				}
				wait_event.WaitOne ();
			}

			public string ReadLine ()
			{
				lock (this) {
					if (lines.Count < 1)
						return null;
					else
						return (string) lines.Dequeue ();
				}
			}
		}
	}
}
