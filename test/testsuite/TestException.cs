using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestException : DebuggerTestFixture
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
			int invalid_op_catchpoint = AssertCatchpoint ("InvalidOperationException");
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

			// Stop when `MyException' is thrown.
			int my_exc_catchpoint = AssertCatchpoint ("MyException");

			AssertExecute ("continue");

			AssertCaughtException (thread, "X.ThrowMy()", GetLine ("throw my"));

			AssertExecute ("continue");
			AssertTargetOutput ("MY EXCEPTION: MyException");

			AssertHitBreakpoint (thread, "main3", "X.Main()");

			// Make it a catchpoint for unhandled `MyException' exceptions only.
			AssertExecute ("delete " + my_exc_catchpoint);
			AssertExecute ("delete " + invalid_op_catchpoint);
			my_exc_catchpoint = AssertUnhandledCatchpoint ("MyException");

			// `MyException' is thrown, but we don't stop since we're only interested
			// in unhandled instances of it.

			AssertExecute ("continue");
			AssertTargetOutput ("True");
			AssertHitBreakpoint (thread, "main4", "X.Main()");
			AssertPrint (thread, "catched", "(bool) true");

			AssertExecute ("continue");
			AssertCaughtUnhandledException (thread, "X.ThrowMy()", GetLine ("throw my"));

			AssertExecute ("continue");
			AssertNoTargetOutput ();
			AssertTargetExited (thread.Process);
		}
	}
}
