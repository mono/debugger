using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

using Mono.Debugger;
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

			AssertStopped (process.MainThread, 0, "X.Main()", 218);

			AssertNoTargetOutput ();
			AssertNoDebuggerOutput ();

			int bpt_simple = AssertBreakpoint ("120");
			int bpt_boxed_value = AssertBreakpoint ("132");

			Execute ("continue");
			AssertHitBreakpoint (process.MainThread, bpt_simple, "X.Simple()", 120);

			Execute ("next");
			AssertTargetOutput ("5");
			Execute ("continue");
			AssertTargetOutput ("7");
			AssertTargetOutput ("0.7142857");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();
			Execute ("kill");
		}
	}
}
