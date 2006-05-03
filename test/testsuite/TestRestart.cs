using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestRestart : TestSuite
	{
		public TestRestart ()
			: base ("TestRestart")
		{ }

		const int line_main = 7;
		const int line_test = 15;
		const int line_bar = 23;

		int bpt_test;
		int bpt_bar;

		[Test]
		[Category("Session")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			bpt_test = AssertBreakpoint (line_test);
			bpt_bar = AssertBreakpoint ("Foo.Bar");

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Session")]
		public void Restarted ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Session")]
		public void SecondRestart ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);
			AssertExecute ("disable " + bpt_test);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}
	}
}
