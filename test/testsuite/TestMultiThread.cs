using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestMultiThread : TestSuite
	{
		public TestMultiThread ()
			: base ("TestMultiThread")
		{ }

		const int LineMain = 43;
		const int LineLoop = 25;

		int bpt_loop;

		[Test]
		[Category("Threads")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "X.Main()", LineMain);

			bpt_loop = AssertBreakpoint (LineLoop);

			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", LineMain + 1);

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertStopped (thread, "X.Main()", LineMain + 2);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertExecute ("continue");
			AssertTargetOutput ("Loop: child 1");
			AssertHitBreakpoint (child, bpt_loop, "X.LoopDone()", LineLoop);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == child);

			AssertExecute ("continue");
			AssertTargetOutput ("Loop: main 1");
			AssertHitBreakpoint (thread, bpt_loop, "X.LoopDone()", LineLoop);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertExecute ("continue -wait -thread " + thread.ID);
			AssertTargetOutput ("Loop: child 2");

			bool child_event = false, thread_event = false;

			while (!child_event || !thread_event) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_loop)) {
						if ((e_thread == thread) && !thread_event) {
							thread_event = true;
							continue;
						} else if ((e_thread == child) && !child_event) {
							child_event = true;
							continue;
						}
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertTargetOutput ("Loop: main 2");
			AssertNoTargetOutput ();

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertFrame (thread, "X.LoopDone()", LineLoop);
			AssertFrame (child, "X.LoopDone()", LineLoop);

			AssertExecute ("kill");

		}
	}
}
