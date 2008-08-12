using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestActivateBreakpoints : TestSuite
	{
		public TestActivateBreakpoints ()
			: base ("TestActivateBreakpoints")
		{ }

		[Test]
		[Category("GUI")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main(string[])");

			int bpt_main2 = (int) AssertExecute ("break -gui " + GetLine ("main2"));

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main2, "X.Main(string[])", GetLine ("main2"));

			AssertExecute ("bg");
			AssertTargetOutput ("True");

			int bpt_loop = (int) AssertExecute ("break -gui " + GetLine ("loop"));
			AssertHitBreakpoint (thread, bpt_loop, "X.Main(string[])", GetLine ("loop"));
			AssertExecute ("set loop = false");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "stop", "X.Main(string[])");

			AssertExecute ("bg");
			AssertTargetOutput ("Stop");

			int bpt_second_loop = (int) AssertExecute ("break -gui " + GetLine ("second loop"));
			AssertHitBreakpoint (thread, bpt_second_loop, "X.Main(string[])", GetLine ("second loop"));

			AssertExecute ("set loop = false");
			AssertExecute ("disable " + bpt_second_loop);
			AssertExecute ("continue");

			AssertTargetOutput ("Done");
			AssertTargetExited (process);
		}
	}
}
