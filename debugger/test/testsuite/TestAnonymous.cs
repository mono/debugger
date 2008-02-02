using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAnonymous : TestSuite
	{
		public TestAnonymous ()
			: base ("TestAnonymous")
		{ }

		int bpt_test1;
		int bpt_test2;
		int bpt_test1_foo;

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", GetLine ("main"));
			bpt_test1 = AssertBreakpoint (GetLine ("test1"));
			bpt_test2 = AssertBreakpoint (GetLine ("test2"));

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_test1, "X.Test1(R,int)", GetLine ("test1"));

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, bpt_test2, "X.Test1(R,int)", GetLine ("test2"));

			AssertExecute ("step");
			// FIXME: Print a better method name
			AssertStopped (thread, "X/<>c__CompilerGenerated2`1<R>.<Test1>c__3()",
				       GetLine ("test1 foo"));

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "r", "(long) 500");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, bpt_test1, "X.Test1(R,int)", GetLine ("test1"));

			AssertPrint (thread, "b", "(int) 1");

			AssertExecute ("disable " + bpt_test1);
			AssertExecute ("disable " + bpt_test2);

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertTargetOutput ("1");
			AssertTargetOutput ("500");
			AssertTargetOutput ("-1");
			AssertTargetOutput ("500");
			AssertTargetExited (thread.Process);
		}
	}
}
