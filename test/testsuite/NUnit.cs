using System;
using System.IO;
using System.Collections;
using SD = System.Diagnostics;
using ST = System.Threading;
using System.Text;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	public enum DebuggerEventType {
		TargetEvent,
		ThreadCreated,
		ThreadExited,
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
			: base (true, config, options)
		{
			this.inferior_stdout = inferior_stdout;
			this.inferior_stderr = inferior_stderr;

			queue = Queue.Synchronized (new Queue ());
			wait_event = new ST.ManualResetEvent (false);

			Style = style_nunit = new StyleNUnit (this);
			style_nunit.TargetEventEvent += delegate (Thread thread, TargetEventArgs args) {
				AddEvent (new DebuggerEvent (DebuggerEventType.TargetEvent, thread, args));
			};
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
			for (int i = 0; i < 3; i++) {
				lock (queue.SyncRoot) {
					if (queue.Count > 0)
						return (DebuggerEvent) queue.Dequeue ();

					wait_event.Reset ();
				}

				wait_event.WaitOne (2500, false);
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

		static TestSuite ()
		{
			Report.Initialize ();
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

		public NUnitInterpreter Interpreter {
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

		public void Compile (string filename)
		{
			SD.ProcessStartInfo start = new SD.ProcessStartInfo (
				BuildInfo.mcs, "-debug " + filename);
			start.UseShellExecute = false;
			start.RedirectStandardOutput = true;
			start.RedirectStandardError = true;

			SD.Process child = SD.Process.Start (start);
			child.WaitForExit ();

			if (child.ExitCode != 0)
				Assert.Fail ("Compilation of {0} exited with error: {1}\n{2}",
					     filename, child.ExitCode, child.StandardError.ReadToEnd ());
		}

		[TestFixtureSetUp]
		public virtual void SetUp ()
		{
			interpreter = new NUnitInterpreter (
				config, options, inferior_stdout, inferior_stderr);

			engine = interpreter.DebuggerEngine;
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
			return AssertBreakpoint (location.ToString ());
		}

		public int AssertBreakpoint (string location)
		{
			object result = AssertExecute ("break " + location);
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
	}

	internal class LineReader
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
			wait_event.WaitOne (2500, false);
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
