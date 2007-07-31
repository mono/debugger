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
		{ }

		const int LineHello = 9;
		const int LineMain = 17;
		const int LineUnload = 26;

		int bpt_unload;
		int bpt_hello;

		[Test]
		[Category("AppDomain")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			bpt_unload = AssertBreakpoint (LineUnload);
			bpt_hello = AssertBreakpoint ("Foo.Hello");

			AssertStopped (thread, "X.Main()", LineMain);
			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", LineMain+1);
			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", LineMain+2);

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertStopped (thread, "X.Main()", LineMain+3);

			AssertExecute ("continue");

			AssertTargetOutput ("TEST: Foo");

			AssertHitBreakpoint (thread, bpt_hello, "Foo.Hello()", LineHello);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World from Test!");
			AssertStopped (thread, "Foo.Hello()", LineHello + 1);

			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_hello, "Foo.Hello()", LineHello);
			AssertExecute ("next");
			AssertTargetOutput ("Hello World from TestAppDomain.exe!");
			AssertStopped (thread, "Foo.Hello()", LineHello + 1);

			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_unload, "X.Main()", LineUnload);
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
