using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAppDomain : TestSuite
	{
		public TestAppDomain ()
			: base ("TestAppDomain")
		{
			Config.BrokenThreading = false;
			Config.StayInThread = true;
		}

		int bpt_hello;

		[Test]
		[Category("AppDomain")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "main", "X.Main()");

			bpt_hello = AssertBreakpoint ("Hello.World");

			AssertExecute ("next");
			AssertStopped (thread, "main+1", "X.Main()");
			AssertExecute ("next");
			AssertStopped (thread, "main+2", "X.Main()");

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertStopped (thread, "main+3", "X.Main()");

			AssertExecute ("continue");

			AssertTargetOutput ("TEST: Hello");

			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from Test!");

			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));
			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from TestAppDomain.exe!");

			AssertHitBreakpoint (thread, "main2", "X.Main()");

			AssertExecute ("delete " + bpt_hello);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from Test!");
			AssertTargetOutput ("Hello World from TestAppDomain.exe!");

			AssertHitBreakpoint (thread, "unload", "X.Main()");
			AssertExecute ("next -wait");

			bool child_exited = false, child_thread_exited = false;
			bool main_stopped = false, temp_thread_created = false;
			bool temp_exited = false, temp_thread_exited = false;

			Thread temp_thread = null;

			while (!child_exited || !child_thread_exited || !main_stopped ||
			       !temp_thread_created || !temp_exited || !temp_thread_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == child) &&
					    (args.Type == TargetEventType.TargetExited)) {
						child_exited = true;
						continue;
					} else if ((e_thread == temp_thread) &&
						   (args.Type == TargetEventType.TargetExited)) {
						temp_exited = true;
						continue;
					} else if ((e_thread == thread) &&
						   (args.Type == TargetEventType.TargetStopped) &&
						   ((int) args.Data == 0)) {
						main_stopped = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					temp_thread = (Thread) e.Data;
					temp_thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					Thread e_thread = (Thread) e.Data;
					if (e_thread == child) {
						child_thread_exited = true;
						continue;
					} else if (e_thread == temp_thread) {
						temp_thread_exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("AppDomain")]
		public void Main2 ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "main", "X.Main()");

			bpt_hello = AssertBreakpoint ("Hello.World");

			AssertExecute ("next");
			AssertStopped (thread, "main+1", "X.Main()");
			AssertExecute ("next");
			AssertStopped (thread, "main+2", "X.Main()");

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertStopped (thread, "main+3", "X.Main()");

			AssertExecute ("continue");

			AssertTargetOutput ("TEST: Hello");

			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));
			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from Test!");

			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));
			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from TestAppDomain.exe!");

			AssertHitBreakpoint (thread, "main2", "X.Main()");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));
			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from Test!");
			AssertHitBreakpoint (thread, bpt_hello,
					     "Hello.World()", GetLine ("hello"));
			AssertExecute ("continue");
			AssertTargetOutput ("Hello World from TestAppDomain.exe!");

			AssertHitBreakpoint (thread, "unload", "X.Main()");
			AssertExecute ("next -wait");

			bool child_exited = false, child_thread_exited = false;
			bool main_stopped = false, temp_thread_created = false;
			bool temp_exited = false, temp_thread_exited = false;

			Thread temp_thread = null;

			while (!child_exited || !child_thread_exited || !main_stopped ||
			       !temp_thread_created || !temp_exited || !temp_thread_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == child) &&
					    (args.Type == TargetEventType.TargetExited)) {
						child_exited = true;
						continue;
					} else if ((e_thread == temp_thread) &&
						   (args.Type == TargetEventType.TargetExited)) {
						temp_exited = true;
						continue;
					} else if ((e_thread == thread) &&
						   (args.Type == TargetEventType.TargetStopped) &&
						   ((int) args.Data == 0)) {
						main_stopped = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					temp_thread = (Thread) e.Data;
					temp_thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadExited) {
					Thread e_thread = (Thread) e.Data;
					if (e_thread == child) {
						child_thread_exited = true;
						continue;
					} else if (e_thread == temp_thread) {
						temp_thread_exited = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertTargetExited (thread.Process);
		}

	}
}
