using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestLineHidden : TestSuite
	{
		public TestLineHidden ()
			: base ("TestLineHidden")
		{ }

#if ENABLE_KAHALO
		[Test]
		[Category("SSE")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test", "X.Test(X)");

			AssertExecute ("next");
			AssertTargetOutput ("Start!");
			AssertTargetOutput ("Hidden!");
			AssertTargetOutput ("Last hidden line!");
			AssertStopped (thread, "test end", "X.Test(X)");

			AssertExecute ("continue");
			AssertTargetOutput ("End!");
			AssertHitBreakpoint (thread, "test", "X.Test(X)");

			AssertExecute ("next");
			AssertTargetOutput ("Start!");
			AssertTargetOutput ("Hidden!");
			AssertNoTargetOutput ();
			AssertSegfault (thread, "test foo", "X.Test(X)");

			AssertExecute ("kill");

			AssertTargetExited (process);
		}
#endif
	}
}
