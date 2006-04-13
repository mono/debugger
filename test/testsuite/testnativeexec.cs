using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class testnativeexec : TestSuite
	{
		public testnativeexec ()
			: base ("testnativeexec", "testnativeexec.c")
		{ }

		const int line_main = 13;
		const int line_waitpid = 26;
		const int line_child = 20;

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

			Thread execd_child = AssertProcessExecd ();

			AssertExecute ("next");
			AssertTargetOutput ("Hello World!");
			AssertProcessExited (execd_child.Process);
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
		        int child_bpt = AssertBreakpoint ("-group global " + line_child);
			AssertExecute ("next");

			Thread child = AssertProcessCreated ();
			AssertStopped (thread, "main", line_main + 1);
			AssertHitBreakpoint (child, child_bpt, "main", line_child);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertPrint (child, "pid", "(pid_t) 0");

			AssertExecute ("background -thread " + child.ID);

			AssertExecute ("next");
			AssertStopped (thread, "main", line_waitpid);

			Thread execd_child = AssertProcessExecd ();

			AssertExecute ("next");
			AssertTargetOutput ("Hello World!");
			AssertProcessExited (execd_child.Process);
			AssertStopped (thread, "main", line_waitpid + 1);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Fork")]
		public void Continue ()
		{
			Process process = Interpreter.Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", line_main);
		        int bpt_end = AssertBreakpoint (line_waitpid + 1);
			AssertExecute ("background");

			Thread execd_child = AssertProcessForkedAndExecd ();
			AssertTargetOutput ("Hello World!");
			AssertProcessExited (execd_child.Process);

			AssertHitBreakpoint (thread, bpt_end, "main", line_waitpid + 1);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}
	}
}
