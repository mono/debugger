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

		const int LineMain = 41;
		const int LineXCCtor = 29;
		const int LineBarCCtor = 16;
		const int LineBarHello = 21;

		int bpt_x_cctor;
		int bpt_bar_cctor;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			bpt_x_cctor = AssertBreakpoint (LineXCCtor);
			bpt_bar_cctor = AssertBreakpoint (LineBarCCtor);

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertHitBreakpoint (thread, bpt_x_cctor, "X..cctor()", LineXCCtor);

			Backtrace bt_x_cctor = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_x_cctor.Count == 1);
			AssertFrame (bt_x_cctor [0], 0, "X..cctor()", LineXCCtor);

			AssertExecute ("continue");
			AssertTargetOutput ("X STATIC CCTOR!");

			AssertStopped (thread, "X.Main()", LineMain);

			Backtrace bt_main = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_main.Count == 1);
			AssertFrame (bt_main [0], 0, "X.Main()", LineMain);

			AssertExecute ("next");
			AssertTargetOutput ("Hello World!");
			AssertTargetOutput ("Second line.");
			AssertTargetOutput ("Hello World!");
			AssertTargetOutput ("Second line.");

			AssertStopped (thread, "X.Main()", LineMain + 1);

			AssertExecute ("step");

			AssertHitBreakpoint (thread, bpt_bar_cctor, "Bar..cctor()", LineBarCCtor);

			Backtrace bt_bar_cctor = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_bar_cctor.Count == 5);
			AssertFrame (bt_bar_cctor [0], 0, "Bar..cctor()", LineBarCCtor);
			AssertFrame (bt_bar_cctor [4], 4, "X.Main()", LineMain + 1);

			AssertExecute ("continue");
			AssertTargetOutput ("BAR STATIC CCTOR!");

			AssertStopped (thread, "X.Main()", LineMain + 1);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}
	}
}
