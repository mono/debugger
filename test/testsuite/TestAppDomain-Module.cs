using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAppDomainModule : TestSuite
	{
		public TestAppDomainModule ()
			: base ("TestAppDomain-Module")
		{ }

		const int LineHelloWorld = 7;

		const int LineMain = 9;
		const int LineMain2 = 12;
		const int LineUnload = 14;
		const int LineEnd = 16;

		int bpt_unload;
		int bpt_end;
		int bpt_world;

		[Test]
		[Category("AppDomain")]
		public void Main ()
		{
			bpt_world = AssertBreakpoint ("Hello.World");

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "X.Main()", LineMain);

			bpt_unload = AssertBreakpoint (LineUnload);
			bpt_end = AssertBreakpoint (LineEnd);

			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", LineMain+1);

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertStopped (thread, "X.Main()", LineMain2);

			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_world, "Hello.World()", LineHelloWorld);
			AssertExecute ("continue");

			AssertTargetOutput ("Hello World from Test!");

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

			AssertFrame (thread, "X.Main()", LineEnd);

			AssertExecute ("next");
			AssertTargetOutput ("UNLOADED!");
			AssertStopped (thread, "X.Main()", LineEnd + 1);

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
