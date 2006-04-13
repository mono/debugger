using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestExec : TestSuite
	{
		public TestExec ()
			: base ("TestExec.exe", "TestExec.cs",
				BuildDirectory + "/testnativechild")
		{ }

		const int line_main = 8;
		const int line_main_2 = 10;

		int bpt_main;

		[Test]
		[Category("Fork")]
		public void NativeChild ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(System.String[])", line_main);
			bpt_main = AssertBreakpoint (line_main_2);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main, "X.Main(System.String[])", line_main_2);

			AssertExecute ("next");

			Thread child = AssertProcessForkedAndExecd ();
			AssertStopped (thread, "X.Main(System.String[])", line_main_2 + 1);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World!");
			AssertProcessExited (child.Process);
			AssertStopped (thread, "X.Main(System.String[])", line_main_2 + 2);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Fork")]
		public void ManagedChild ()
		{
			Interpreter.Options.InferiorArgs = new string [] {
				BuildDirectory + "/TestExec.exe",
				MonoExecutable, "--inside-mdb",
				BuildDirectory + "/TestChild.exe" };

			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(System.String[])", line_main);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main, "X.Main(System.String[])", line_main_2);

			AssertExecute ("next");

			Thread child = AssertProcessForkedAndExecd ();
			AssertStopped (thread, "X.Main(System.String[])", line_main_2 + 1);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World");
			AssertProcessExited (child.Process);
			AssertStopped (thread, "X.Main(System.String[])", line_main_2 + 2);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}
	}
}
