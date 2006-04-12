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

		[Test]
		[Category("Fork")]
		public void NativeChild ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(System.String[])", line_main);
			AssertExecute ("next");

			Thread child = AssertProcessForkedAndExecd ();
			AssertStopped (thread, "X.Main(System.String[])", line_main + 1);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World!");
			AssertProcessExited (child.Process);
			AssertStopped (thread, "X.Main(System.String[])", line_main + 2);

			AssertExecute ("continue");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}
	}
}
