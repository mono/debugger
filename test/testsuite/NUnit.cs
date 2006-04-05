using System;
using System.Collections;
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

		static Regex breakpoint_regex = new Regex (@"^Breakpoint ([0-9]+) at ([^\n\r]+)$");
		static Regex stopped_regex = new Regex (@"^Thread @([0-9]+) stopped at ([^\n\r]+)\.$");
		static Regex hit_breakpoint_regex = new Regex (@"^Thread @([0-9]+) hit breakpoint ([0-9]+) at ([^\n\r]+)\.$");
		static Regex frame_regex = new Regex (@"^#([0-9]+): 0x[0-9A-Fa-f]+ in (.*)$");
		static Regex func_source_regex = new Regex (@"^(.*) at (.*):([0-9]+)$");
		static Regex func_offset_regex = new Regex (@"^(.*)\+0x([0-9A-Fa-f]+)$");

		protected TestSuite (string application)
		{
			options = CreateOptions (application);

			debugger_output = new LineReader ();
			inferior_stdout = new LineReader ();
			inferior_stderr = new LineReader ();
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		[TestFixtureSetUp]
		public void SetUp ()
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
		public void TearDown ()
		{
			interpreter.Kill ();
			interpreter = null;
		}

		private static DebuggerOptions CreateOptions (string application)
		{
			DebuggerOptions options = new DebuggerOptions ();

			options.IsScript = true;
			options.File = application;
			options.InferiorArgs = new string [1];
			options.InferiorArgs [0] = options.File;

			return options;
		}

		public void Execute (string command)
		{
			parser.Reset ();
			parser.Append (command);
			parser.Execute ();
		}

		public void AssertFrame (Thread thread, string function, int line)
		{
			try {
				StackFrame frame = thread.CurrentFrame;
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

		public void AssertTargetOutput (string line)
		{
			string output = inferior_stdout.ReadLine ();
			if (output == null) {
				Assert.Fail ("No target output.");
				return;
			}

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
			string output = debugger_output.ReadLine ();
			if (output == null) {
				Assert.Fail ("No debugger output.");
				return;
			}

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
			if (!match.Success) {
				Assert.Fail ("Received unknown stack frame `{0}'.", frame);
				return;
			}

			int index = Int32.Parse (match.Groups [1].Value);
			string func = match.Groups [2].Value;

			Match source_match = func_source_regex.Match (func);
			if (!source_match.Success) {
				Assert.Fail ("Received unknown stack frame `{0}'.", frame);
				return;
			}

			func = source_match.Groups [1].Value;
			string file = source_match.Groups [2].Value;
			int line = Int32.Parse (source_match.Groups [3].Value);

			Match offset_match = func_offset_regex.Match (func);
			if (offset_match != null)
				func = offset_match.Groups [1].Value;

			if (index != exp_index) {
				Assert.Fail ("Received frame {0}, but expected {1}.", index, exp_index);
				return;
			}

			if (func != exp_func) {
				Assert.Fail ("Target stopped in function `{0}', but expected `{1}'.",
					     func, exp_func);
				return;
			}

			if (line != exp_line) {
				Assert.Fail ("Target stopped in line {0}, but expected {1}.",
					     line, exp_line);
				return;
			}
		}

		public void AssertStopped (Thread exp_thread, int exp_index,
					   string exp_func, int exp_line)
		{
			AssertFrame (exp_thread, exp_func, exp_line);

			string output = debugger_output.ReadLine ();
			if (output == null) {
				Assert.Fail ("Target not stopped.");
				return;
			}

			Match match = stopped_regex.Match (output);
			if (!match.Success) {
				Assert.Fail ("Target not stopped.");
				return;
			}

			int thread = Int32.Parse (match.Groups [1].Value);
			string frame = match.Groups [2].Value;

			if (thread != exp_thread.ID) {
				Assert.Fail ("Thread {0} stopped at {1}, but expected thread {2} to stop.",
					     thread, frame, exp_thread.ID);
				return;
			}

			AssertFrame (frame, exp_index, exp_func, exp_line);
		}

		public void AssertHitBreakpoint (Thread exp_thread, int exp_index,
						 string exp_func, int exp_line)
		{
			AssertFrame (exp_thread, exp_func, exp_line);

			string output = debugger_output.ReadLine ();
			if (output == null) {
				Assert.Fail ("Target not stopped.");
				return;
			}

			Match match = hit_breakpoint_regex.Match (output);
			if (!match.Success) {
				Assert.Fail ("Target not stopped.");
				return;
			}

			int thread = Int32.Parse (match.Groups [1].Value);
			int index = Int32.Parse (match.Groups [2].Value);
			string frame = match.Groups [3].Value;

			if (thread != exp_thread.ID) {
				Assert.Fail ("Thread {0} stopped at {1}, but expected thread {2} to stop.",
					     thread, frame, exp_thread.ID);
				return;
			}

			if (index != exp_index) {
				Assert.Fail ("Thread {0} hit breakpoint {1}, but expected {2}.",
					     thread, index, exp_index);
				return;
			}

			AssertFrame (frame, 0, exp_func, exp_line);
		}

		public int AssertBreakpoint (string location)
		{
			AssertNoDebuggerOutput ();
			Execute ("break " + location);

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
			string text = null;
			try {
				ScriptingContext context = GetContext (thread);

				object obj = EvaluateExpression (context, expression);
				text = context.FormatObject (obj, DisplayFormat.Object);
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

			public void Add (string text)
			{
			again:
				int pos = text.IndexOf ('\n');
				if (pos < 0)
					current_line += text;
				else {
					current_line += text.Substring (0, pos);
					lines.Enqueue (current_line);
					current_line = "";
					text = text.Substring (pos + 1);
					if (text.Length == 0)
						return;
					goto again;
				}
			}

			public string ReadLine ()
			{
				if (lines.Count < 1)
					return null;
				else
					return (string) lines.Dequeue ();
			}
		}
	}

	[TestFixture]
	public class SampleTest : TestSuite
	{
		public SampleTest ()
			: base ("../TestManagedTypes.exe")
		{ }

		[Test]
		public void Hello ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, 0, "X.Main()", 218);

			AssertNoTargetOutput ();
			AssertNoDebuggerOutput ();

			const int line_simple = 120;
			const int line_boxed_value = 132;
			const int line_boxed_ref = 142;
			const int line_simple_array = 150;
			const int line_multi_value_array = 157;
			const int line_string_array = 164;
			const int line_multi_string_array = 172;
			const int line_struct_type = 179;
			const int line_class_type = 186;
			const int line_inherited_class_type = 195;
			const int line_complex_struct_type = 205;
			const int line_function_struct_type = 213;

			int bpt_simple = AssertBreakpoint (line_simple.ToString ());
			int bpt_boxed_value = AssertBreakpoint (line_boxed_value.ToString ());
			int bpt_boxed_ref = AssertBreakpoint (line_boxed_ref.ToString ());
			int bpt_simple_array = AssertBreakpoint (line_simple_array.ToString ());
			int bpt_multi_value_array = AssertBreakpoint (line_multi_value_array.ToString ());
			int bpt_string_array = AssertBreakpoint (line_string_array.ToString ());
			int bpt_multi_string_array = AssertBreakpoint (line_multi_string_array.ToString ());
			int bpt_struct_type = AssertBreakpoint (line_struct_type.ToString ());
			int bpt_class_type = AssertBreakpoint (line_class_type.ToString ());
			int bpt_inherited_class_type = AssertBreakpoint (line_inherited_class_type.ToString ());
			int bpt_complex_struct_type = AssertBreakpoint (line_complex_struct_type.ToString ());
			int bpt_function_struct_type = AssertBreakpoint (line_function_struct_type.ToString ());

			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_simple, "X.Simple()", line_simple);

			AssertType (thread, "a", "System.Int32");
			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertType (thread, "b", "System.Int64");
			AssertPrint (thread, "b", "(System.Int64) 7");
			AssertType (thread, "f", "System.Single");
			AssertPrint (thread, "f", "(System.Single) 0.7142857");
			AssertType (thread, "hello", "System.String");
			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");

			Execute ("set a = 9");
			Execute ("set hello = \"Monkey\"");

			AssertPrint (thread, "a", "(System.Int32) 9");
			AssertPrint (thread, "hello", "(System.String) \"Monkey\"");

			Execute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("7");
			AssertTargetOutput ("0.7142857");
			AssertTargetOutput ("Monkey");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_value, "X.BoxedValueType()",
					     line_boxed_value);

			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertPrint (thread, "boxed_a", "(object) &(System.Int32) 5");
			AssertPrint (thread, "*boxed_a", "(System.Int32) 5");

			Execute ("continue");
			AssertTargetOutput ("5");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_ref, "X.BoxedReferenceType()",
					     line_boxed_ref);

			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "boxed_hello", "(object) &(System.String) \"Hello World\"");
			AssertPrint (thread, "*boxed_hello", "(System.String) \"Hello World\"");

			Execute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_simple_array, "X.SimpleArray()",
					     line_simple_array);

			AssertPrint (thread, "a", "(System.Int32[]) [ 3, 4, 5 ]");
			AssertPrint (thread, "a[1]", "(System.Int32) 4");
			Execute ("set a[2] = 9");
			AssertPrint (thread, "a[2]", "(System.Int32) 9");

			Execute ("continue");
			AssertTargetOutput ("9");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_value_array,
					     "X.MultiValueTypeArray()", line_multi_value_array);

			AssertPrint (thread, "a", "(System.Int32[,]) [ [ 6, 7, 8 ], [ 9, 10, 11 ] ]");
			AssertPrintException (thread, "a[1]",
					      "Index of array expression `a' out of bounds.");
			AssertPrint (thread, "a[1,2]", "(System.Int32) 11");
			AssertPrintException (thread, "a[2]",
					      "Index of array expression `a' out of bounds.");
			Execute ("set a[1,2] = 50");

			Execute ("continue");
			AssertTargetOutput ("50");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_string_array, "X.StringArray()",
					     line_string_array);

			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"World\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"World\"");
			Execute ("set a[1] = \"Trier\"");
			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"Trier\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"Trier\"");

			Execute ("continue");
			AssertTargetOutput ("System.String[]");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_string_array, "X.MultiStringArray()",
					     line_multi_string_array);

			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Monkeys\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Monkeys\"");
			Execute ("set a[2,1] = \"Primates\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");
			Execute ("set a[0,1] = \"Lions\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[0,1]", "(System.String) \"Lions\"");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");

			Execute ("set a[0,0] = \"Birds\"");
			Execute ("set a[2,0] = \"Dogs\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Birds\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Dogs\", \"Primates\" ] ]");

			Execute ("continue");
			AssertTargetOutput ("System.String[,]");
			AssertTargetOutput ("51.2");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_struct_type, "X.StructType()",
					     line_struct_type);

			AssertPrint (thread, "a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");

			Execute ("continue");
			AssertTargetOutput ("A");
			AssertTargetOutput ("5");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_class_type, "X.ClassType()",
					     line_class_type);

			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			Execute ("continue");
			AssertTargetOutput ("B");
			AssertTargetOutput ("8");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_inherited_class_type,
					     "X.InheritedClassType()", line_inherited_class_type);

			AssertPrint (thread, "c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "(B) c",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			Execute ("continue");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_complex_struct_type,
					     "X.ComplexStructType()", line_complex_struct_type);

			AssertPrint (thread, "d.a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");
			AssertPrint (thread, "d.b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "d.c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "d.s",
				     "(System.String[]) [ \"Eintracht Trier\" ]");

			Execute ("continue");
			AssertTargetOutput ("Eintracht Trier");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_function_struct_type,
					     "X.FunctionStructType()", line_function_struct_type);

			AssertPrint (thread, "e", "(E) { a = 9 }");
			AssertPrint (thread, "e.a", "(System.Int32) 9");
			AssertPrint (thread, "e.Foo (5)", "(System.Int64) 5");

			Execute ("kill");
		}
	}
}
