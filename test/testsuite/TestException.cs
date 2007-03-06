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

		const int LineException = 7;
		const int LineMain = 12;
		const int LineTry = 14;
		const int LineMain2 = 19;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", LineMain);
			AssertCatchpoint ("InvalidOperationException");
			int bpt_main_2 = AssertBreakpoint (LineMain2);
			AssertExecute ("continue");

			AssertCaughtException (thread, "X.Test()", LineException);
			AssertNoTargetOutput ();

			Backtrace bt = thread.GetBacktrace (-1);
			if (bt.Count != 2)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 2);

			AssertFrame (bt [0], 0, "X.Test()", LineException);
			AssertFrame (bt [1], 1, "X.Main()", LineTry);

			AssertExecute ("continue");
			AssertTargetOutput ("EXCEPTION: System.InvalidOperationException");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", LineMain2);
			AssertPrintException (thread, "x.Test()",
					      "Invocation of `x.Test ()' raised an exception: " +
					      "System.InvalidOperationException: Operation is not " +
					      "valid due to the current state of the object\n" +
					      "  at X.Test () [0x00000] in " + FileName + ":" +
					      LineException + " ");

			AssertExecute ("continue");
			AssertTargetOutput ("Done");
			AssertTargetExited (thread.Process);
		}
	}
}
