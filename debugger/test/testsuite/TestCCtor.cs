using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestCCtor : TestSuite
	{
		public TestCCtor ()
			: base ("TestCCtor")
		{ }

		const int LineMain = 28;
		const int LineCCtor = 16;

		int bpt_cctor;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			bpt_cctor = AssertBreakpoint (LineCCtor);

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertHitBreakpoint (thread, bpt_cctor, "X..cctor()", LineCCtor);

			Backtrace bt_cctor = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_cctor.Count == 1);
			AssertFrame (bt_cctor [0], 0, "X..cctor()", LineCCtor);

			AssertExecute ("continue");
			AssertTargetOutput ("STATIC CCTOR!");

			AssertStopped (thread, "X.Main()", LineMain);

			Backtrace bt_main = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_main.Count == 1);
			AssertFrame (bt_main [0], 0, "X.Main()", LineMain);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World!");
			AssertTargetOutput ("Second line.");
			AssertTargetOutput ("Hello World!");
			AssertTargetOutput ("Second line.");
			AssertTargetExited (thread.Process);
		}
	}
}
