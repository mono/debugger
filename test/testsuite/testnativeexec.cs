using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class testnativeexec : DebuggerTestFixture
	{
		public testnativeexec ()
			: base ("testnativeexec", "testnativeexec.c")
		{ }

		const int LineMain = 13;
		const int LineWaitpid = 24;
		const int LineChild = 19;

		int bpt_local_waitpid;
		int bpt_waitpid;
		int bpt_child;

		public override void SetUp ()
		{
			base.SetUp ();
			Config.FollowFork = true;

			bpt_local_waitpid = AssertBreakpoint (String.Format ("-local {0}:{1}", FileName, LineWaitpid+1));
			bpt_waitpid = AssertBreakpoint (String.Format ("-local {0}:{1}", FileName, LineWaitpid));
			bpt_child = AssertBreakpoint (String.Format ("-global {0}:{1}", FileName, LineChild));
		}

		[Test]
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
			AssertExecute ("continue -wait");

			Thread child = AssertProcessCreated ();

			bool exited = false;
			bool thread_created = false;
			bool child_exited = false;
			bool thread_exited = false;
			bool execd = false;
			bool stopped = false;

			Thread execd_child = null;

			while (!exited || !child_exited || !stopped || !execd || !thread_created ||
			       !thread_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						child_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == execd_child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ProcessExecd) {
					if ((Process) e.Data == child.Process) {
						execd = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_local_waitpid)) {
						stopped = true;
						continue;
					} else if ((e_thread == execd_child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineWaitpid + 1);
			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}


		[Test]
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
			AssertExecute ("continue -wait");

			Thread child = AssertProcessCreated ();

			bool exited = false;
			bool thread_created = false;
			bool child_exited = false;
			bool thread_exited = false;
			bool execd = false;
			bool stopped = false;

			Thread execd_child = null;

			while (!exited || !child_exited || !stopped || !execd || !thread_created ||
			       !thread_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						child_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == execd_child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ProcessExecd) {
					if ((Process) e.Data == child.Process) {
						execd = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_local_waitpid)) {
						stopped = true;
						continue;
					} else if ((e_thread == execd_child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineWaitpid + 1);
			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertExecute ("continue");

			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void Breakpoint ()
		{
			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("enable " + bpt_waitpid);
			AssertExecute ("enable " + bpt_local_waitpid);
			AssertExecute ("enable " + bpt_child);

			// int bpt_child = AssertBreakpoint ("-global " + LineChild);

			AssertExecute ("continue -bg");

			Thread child = AssertProcessCreated ();

			bool child_stopped = false;
			bool reached_waitpid = false;

			while (!child_stopped || !reached_waitpid) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type != DebuggerEventType.TargetEvent)
					Assert.Fail ("Received unexpected event {0}", e);

				Thread e_thread = (Thread) e.Data;
				TargetEventArgs args = (TargetEventArgs) e.Data2;

				if ((e_thread == thread) &&
				    (args.Type == TargetEventType.TargetHitBreakpoint) &&
				    ((int) args.Data == bpt_waitpid)) {
					reached_waitpid = true;
					continue;
				} else if ((e_thread == child) &&
					   (args.Type == TargetEventType.TargetHitBreakpoint) &&
					   ((int) args.Data == bpt_child)) {
					child_stopped = true;
					continue;
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineWaitpid);
			AssertFrame (child, "main", LineChild);

			AssertPrint (thread, "pid", String.Format ("(pid_t) {0}", child.PID));
			AssertPrint (child, "pid", "(pid_t) 0");

			AssertExecute ("background -thread " + child.ID);
			AssertExecute ("continue -wait");

			Thread execd_child = null;
			bool exited = false;
			bool child_execd = false;
			bool child_exited = false;
			bool thread_created = false;
			bool thread_exited = false;
			bool stopped = false;

			while (!exited || !stopped || !child_execd || !thread_created ||
			       !child_exited || !thread_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExited) {
					if ((Process) e.Data == child.Process) {
						exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == execd_child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ProcessExecd) {
					if ((Process) e.Data == child.Process) {
						child_execd = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_local_waitpid)) {
						stopped = true;
						continue;
					} else if ((e_thread == execd_child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						child_exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertFrame (thread, "main", LineWaitpid + 1);
			AssertTargetOutput ("Hello World!");
			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
