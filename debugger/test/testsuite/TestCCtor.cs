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
		int bpt_bar_hello;

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

			//
			// Dublin Milestone I (completed): "break-before-main"
			//
			// Insert a breakpoint in the main class'es ..cctor before starting the
			// application; make sure we correctly stop in that ..cctor before reaching
			// main.
			//

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

			//
			// Dublin Milestone II (completed): "interrupted-trampolines"
			//
			// Step into a method which isn't compiled yet where that method's
			// static class ..cctor causes the debugger to stop (because of a breakpoint).
			//

			AssertExecute ("step");

			AssertHitBreakpoint (thread, bpt_bar_cctor, "Bar..cctor()", LineBarCCtor);

			Backtrace bt_bar_cctor = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_bar_cctor.Count == 5);
			AssertFrame (bt_bar_cctor [0], 0, "Bar..cctor()", LineBarCCtor);
			AssertFrame (bt_bar_cctor [4], 4, "X.Main()", LineMain + 1);

			//
			// Dublin Milestone III (completed on i386): "recursive-callbacks"
			//
			// We stopped in Bar's static ..cctor.  The important point here is that
			// we previously attempted to step into Bar.Hello() which wan't compiled
			// at that time.  The debugger triggered a compilation which was interrupted
			// because we hit that breakpoint.
			//
			// Now we attempt to do something which'll trigger a callback.
			//
			// The new code also keeps a correct callback stack which is used for stack
			// unwinding; we can now correctly unwind the stack across callback boundaries.
			//

			// This triggers a recursive callback.
			bpt_bar_hello = AssertBreakpoint ("Bar.Hello");

			AssertExecute ("continue");
			AssertTargetOutput ("BAR STATIC CCTOR!");

			AssertStopped (thread, "X.Main()", LineMain + 1);

			bt_main = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt_main.Count == 1);
			AssertFrame (bt_main [0], 0, "X.Main()", LineMain + 1);

			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_bar_hello, "Bar.Hello()", LineBarHello);

			AssertExecute ("continue");

			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);

			//
			// Dublin Milestone IV (next week):
			//
			// In the situation above, move the class init up when stepping over a breakpoint;
			// ie. initialize the class before acquiring the thread lock and also compile the
			// method without it.  Just acquire the lock to do the trampoline stuff; ie.
			// acquire the lock, remove the breakpoint, let mono_magic_trampoline() patch the
			// callsite, reinsert the breakpoint and remove the lock.
			//
			// The current code will only deadlock in very rare cases, but that task will
			// completely eliminate that and it shouldn't be too hard.
			//
		}
	}
}
