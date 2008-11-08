using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestException : TestSuite
	{
		public TestException ()
			: base ("TestException")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", GetLine ("main"));
			AssertCatchpoint ("InvalidOperationException");
			AssertExecute ("continue");

			AssertCaughtException (thread, "X.Test()", GetLine ("exception"));
			AssertNoTargetOutput ();

			Backtrace bt = thread.GetBacktrace (-1);
			if (bt.Count != 2)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 2);

			AssertFrame (bt [0], 0, "X.Test()", GetLine ("exception"));
			AssertFrame (bt [1], 1, "X.Main()", GetLine ("try") + 1);

			AssertExecute ("continue");
			AssertTargetOutput ("EXCEPTION: System.InvalidOperationException");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "main2", "X.Main()");
			AssertPrintException (thread, "x.Test()",
					      "Invocation of `x.Test ()' raised an exception: " +
					      "System.InvalidOperationException: Operation is not " +
					      "valid due to the current state of the object\n" +
					      "  at X.Test () [0x00000] in " + FileName + ":" +
					      GetLine ("exception") + " ");

			AssertExecute ("continue");
			AssertTargetOutput ("Done");

			AssertHitBreakpoint (thread, "try my", "X.TestMy()");

			AssertCatchpoint ("MyException");

			AssertExecute ("continue");

			AssertCaughtException (thread, "X.ThrowMy()", GetLine ("throw my"));

			AssertExecute ("continue");
			AssertTargetOutput ("MY EXCEPTION: MyException");
			AssertNoTargetOutput ();


			AssertTargetExited (thread.Process);
		}
	}
}
