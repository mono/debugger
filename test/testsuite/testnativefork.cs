using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class testnativefork : DebuggerTestFixture
	{
		public testnativefork ()
			: base ("testnativefork", "testnativefork.c")
		{
			Config.ThreadingModel = ThreadingModel.Single;
		}

		const int LineMain = 12;
		const int LineWaitpid = 16;
		const int LineChild = 14;

		int bpt_waitpid;
		int bpt_child;

		public override void SetUp ()
		{
			base.SetUp ();
			Config.FollowFork = true;
			Config.ThreadingModel = ThreadingModel.Single;

			bpt_child = AssertBreakpoint (String.Format ("-global {0}:{1}", FileName, LineChild));
			bpt_waitpid = AssertBreakpoint (String.Format ("-local {0}:{1}", FileName, LineWaitpid + 1));
		}

		[Test]
		[Category("NotWorking")]
		[Category("Native")]
		[Category("Fork")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("disable " + bpt_child);
			AssertExecute ("disable " + bpt_waitpid);
			AssertExecute ("next -wait");

			Thread child = AssertProcessCreated ();

			bool exited = false;
			bool child_exited = false;
			bool thread_exited = false;
			bool stopped = false;

			while (!exited || !child_exited || !thread_exited || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						child_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						stopped = true;
						continue;
					} else if ((e_thread == child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineMain + 1);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));

			AssertExecute ("next -wait");
			AssertStopped (thread, "main", LineWaitpid);

			AssertExecute ("next -wait");
			AssertStopped (thread, "main", LineWaitpid + 1);

			AssertExecute ("continue -wait");
			AssertTargetExited (thread.Process);
		}


		[Test]
		[Category("NotWorking")]
		[Category("Native")]
		[Category("Fork")]
		public void Continue ()
		{
			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("disable " + bpt_child);
			AssertExecute ("disable " + bpt_waitpid);
			AssertExecute ("next -wait");

			Thread child = AssertProcessCreated ();

			bool exited = false;
			bool child_exited = false;
			bool thread_exited = false;
			bool stopped = false;

			while (!exited || !child_exited || !thread_exited || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						child_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						stopped = true;
						continue;
					} else if ((e_thread == child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineMain + 1);
			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));

			AssertExecute ("continue -wait");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("NotWorking")]
		[Category("Native")]
		[Category("Fork")]
		public void Breakpoint ()
		{
			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("enable " + bpt_child);
			AssertExecute ("enable " + bpt_waitpid);
			AssertExecute ("next -wait");

			Thread child = AssertProcessCreated ();

			bool child_stopped = false;
			bool stopped = false;

			while (!stopped || !child_stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type != DebuggerEventType.TargetEvent)
					Assert.Fail ("Received unexpected event {0}", e);

				Thread e_thread = (Thread) e.Data;
				TargetEventArgs args = (TargetEventArgs) e.Data2;

				if ((e_thread == thread) &&
				    (args.Type == TargetEventType.TargetStopped)) {
					stopped = true;
					continue;
				} else if ((e_thread == child) &&
					   (args.Type == TargetEventType.TargetHitBreakpoint) &&
					   ((int) args.Data == bpt_child)) {
					child_stopped = true;
					continue;
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineMain + 1);
			AssertFrame (child, "main", LineChild);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertPrint (child, "pid", "(pid_t) 0");

			AssertExecute ("background -thread " + child.ID);
			AssertExecute ("continue -wait -thread " + thread.ID);

			bool exited = false;
			bool child_exited = false;
			bool thread_exited = false;
			stopped = false;

			while (!exited || !child_exited || !thread_exited || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						child_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_waitpid)) {
						stopped = true;
						continue;
					} else if ((e_thread == child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineWaitpid + 1);

			AssertExecute ("continue -wait -thread " + thread.ID);
			AssertTargetExited (thread.Process);
		}
	}
}
