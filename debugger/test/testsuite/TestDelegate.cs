using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestDelegate : TestSuite
	{
		public TestDelegate ()
			: base ("TestDelegate")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 29);
			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", 30);

			AssertPrint (thread, "x.Foo (8)", "(long) 24");
			AssertTargetOutput ("Hello World: 8");
			AssertTargetOutput ("Boston: 8");
			AssertNoTargetOutput ();

			AssertPrint (thread, "x.Foo (9)", "(long) 27");
			AssertTargetOutput ("Hello World: 9");
			AssertTargetOutput ("Boston: 9");
			AssertNoTargetOutput ();

			AssertExecute ("step");
			AssertStopped (thread, "X.foo(int)", 17);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World: 4");
			AssertNoTargetOutput ();
			AssertStopped (thread, "X.foo(int)", 18);
			AssertExecute ("step");
			AssertStopped (thread, "X.boston(int)", 23);
			AssertExecute ("next");
			AssertTargetOutput ("Boston: 4");
			AssertStopped (thread, "X.boston(int)", 24);
			AssertExecute ("step");
			AssertStopped (thread, "X.Main()", 31);

			AssertPrint (thread, "x.Foo (5)", "(long) 15");
			AssertTargetOutput ("Hello World: 5");
			AssertTargetOutput ("Boston: 5");
			AssertNoTargetOutput ();

			AssertPrint (thread, "x.Foo (3)", "(long) 9");
			AssertTargetOutput ("Hello World: 3");
			AssertTargetOutput ("Boston: 3");
			AssertNoTargetOutput ();

			AssertBreakpoint ("-invoke x.Foo");
			AssertExecute ("continue");
			AssertTargetOutput ("Back in main");
			AssertNoTargetOutput ();
			AssertStopped (thread, "X.foo(int)", 17);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World: 11");
			AssertNoTargetOutput ();
			AssertStopped (thread, "X.foo(int)", 18);
			AssertExecute ("step");
			AssertStopped (thread, "X.boston(int)", 23);
			AssertExecute ("next");
			AssertTargetOutput ("Boston: 11");
			AssertNoTargetOutput ();
			AssertStopped (thread, "X.boston(int)", 24);
			AssertExecute ("step");
			AssertStopped (thread, "X.Main()", 33);

			AssertExecute ("continue");
			AssertTargetOutput ("Back again");
			AssertTargetExited (thread.Process);
		}
	}
}
