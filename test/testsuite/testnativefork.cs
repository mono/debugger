using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class testnativefork : TestSuite
	{
		public testnativefork ()
			: base ("testnativefork", "testnativefork.c")
		{ }

		const int line_main = 12;
		const int line_waitpid = 19;
		const int line_child = 15;

		[Test]
		[Category("Fork")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", line_main);
			AssertExecute ("next");

			Thread child = AssertProcessCreated ();
			AssertStopped (thread, "main", line_main + 1);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertExecute ("next");
			AssertStopped (thread, "main", line_waitpid);
			AssertExecute ("next");
			AssertProcessExited (child.Process);
			AssertStopped (thread, "main", line_waitpid + 1);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Fork")]
		public void Breakpoint ()
		{
			Process process = Interpreter.Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", line_main);
		        AssertBreakpoint (line_child);
			AssertExecute ("next");

			Thread child = AssertProcessCreated ();
			AssertStopped (thread, "main", line_main + 1);
			AssertHitBreakpoint (child, -1, "main", line_child);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertPrint (child, "pid", "(pid_t) 0");

			AssertExecute ("background -thread " + child.ID);

			AssertExecute ("next");
			AssertStopped (thread, "main", line_waitpid);
			AssertExecute ("next");
			AssertProcessExited (child.Process);
			AssertStopped (thread, "main", line_waitpid + 1);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}
	}
}
