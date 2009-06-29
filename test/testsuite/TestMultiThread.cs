using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestMultiThread : DebuggerTestFixture
	{
		public TestMultiThread ()
			: base ("TestMultiThread")
		{
			Config.BrokenThreading = true;
		}

		const int LineMain = 51;
		const int LineLoop = 32;
		const int LineSleep = 20;

		int bpt_loop;

		[Test]
		[Category("Threads")]
		public void Main ()
		{
			Process process = Start ();
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
			AssertTargetOutput ("Loop: child 0");
			AssertHitBreakpoint (child, bpt_loop, "X.LoopDone()", LineLoop);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == child);

			AssertPrint (child, "Child.Counter", "(int) 0");
			AssertPrint (child, "Counter", "(int) 0");
			AssertPrint (child, "Parent.Counter", "(int) 0");

			AssertExecute ("continue");
			AssertTargetOutput ("Loop: child 1");
			AssertHitBreakpoint (child, bpt_loop, "X.LoopDone()", LineLoop);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == child);

			AssertPrint (child, "Child.Counter", "(int) 1");
			AssertPrint (child, "Counter", "(int) 1");
			AssertPrint (child, "Parent.Counter", "(int) 0");

			AssertExecute ("continue");
			AssertTargetOutput ("Loop: main 0");
			AssertHitBreakpoint (thread, bpt_loop, "X.LoopDone()", LineLoop);

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertPrint (thread, "Child.Counter", "(int) 2");
			AssertPrint (thread, "Counter", "(int) 0");
			AssertPrint (thread, "Parent.Counter", "(int) 0");

			AssertNoEvent ();
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

			AssertTargetOutput ("Loop: main 1");
			AssertNoTargetOutput ();

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertPrint (thread, "Child.Counter", "(int) 2");
			AssertPrint (thread, "Parent.Counter", "(int) 1");

			AssertFrame (thread, "X.LoopDone()", LineLoop);
			AssertFrame (child, "X.LoopDone()", LineLoop);

			AssertExecute ("continue -wait -thread " + thread.ID);
			AssertTargetOutput ("Loop: child 3");

			child_event = false; thread_event = false;
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

			AssertPrint (thread, "Child.Counter", "(int) 3");
			AssertPrint (thread, "Parent.Counter", "(int) 2");

			AssertFrame (thread, "X.LoopDone()", LineLoop);
			AssertFrame (child, "X.LoopDone()", LineLoop);

			AssertExecute ("continue -wait -thread " + child.ID);
			AssertTargetOutput ("Loop: child 4");

			AssertHitBreakpoint (child, bpt_loop, "X.LoopDone()", LineLoop);
			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == child);

			AssertPrint (child, "Child.Counter", "(int) 4");
			AssertPrint (child, "Parent.Counter", "(int) 3");

			AssertFrame (child, "X.LoopDone()", LineLoop);

			Backtrace bt = thread.GetBacktrace (Backtrace.Mode.Managed, -1);
			Assert.IsTrue (bt.Count == 6);
			AssertFrame (bt [3], 3, "X.Loop()", LineSleep + 1);

			AssertExecute ("continue -thread " + thread.ID);
			AssertTargetOutput ("Loop: child 5");

			AssertHitBreakpoint (child, bpt_loop, "X.LoopDone()", LineLoop);
			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == child);

			AssertPrint (child, "Child.Counter", "(int) 5");
			AssertPrint (child, "Parent.Counter", "(int) 3");

			AssertFrame (child, "X.LoopDone()", LineLoop);

			AssertExecute ("continue -wait -thread " + thread.ID);
			AssertTargetOutput ("Loop: main 3");

			AssertHitBreakpoint (thread, bpt_loop, "X.LoopDone()", LineLoop);
			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertPrint (thread, "Child.Counter", "(int) 6");
			AssertPrint (thread, "Parent.Counter", "(int) 3");

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == thread);

			AssertExecute ("continue -wait -thread " + thread.ID);
			AssertTargetOutput ("Loop: child 6");

			child_event = false; thread_event = false;
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

			AssertTargetOutput ("Loop: main 4");
			AssertNoTargetOutput ();

			AssertPrint (thread, "Child.Counter", "(int) 6");
			AssertPrint (thread, "Parent.Counter", "(int) 4");

			AssertFrame (thread, "X.LoopDone()", LineLoop);
			AssertFrame (child, "X.LoopDone()", LineLoop);

			AssertPrint (thread, "Parent.Test ()", "(int) 4");

			AssertExecute ("kill");
		}
	}
}
