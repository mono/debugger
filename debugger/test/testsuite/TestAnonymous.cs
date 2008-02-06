using System;
using System.Collections.Generic;
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

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "RunTests.Main()", GetLine ("main"));
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1(R,int)");

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, "test1 after foo", "Test1.X.Test1(R,int)");

			AssertExecute ("step");
			// FIXME: Print a better method name
			AssertStopped (thread, "Test1.X/<>c__CompilerGenerated2`1<R>.<Test1>c__3()",
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
			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1(R,int)");

			AssertPrint (thread, "b", "(int) 1");

			AssertExecute ("disable " + GetBreakpoint ("test1"));
			AssertExecute ("disable " + GetBreakpoint ("test1 after foo"));

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
