using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestExec : TestSuite
	{
		public TestExec ()
			: base ("TestExec.exe", "TestExec.cs",
				BuildDirectory + "/testnativechild")
		{ }

		const int line_main = 8;
		const int line_main_3 = 12;

		int bpt_main;

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void NativeChild ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			bpt_main = AssertBreakpoint ("-local " + line_main_3);
			AssertExecute ("continue -wait");

			Thread child = AssertProcessCreated ();
			Thread execd_child = null;

			bool exited = false;
			bool execd = false;
			bool stopped = false;
			bool thread_created = false;
			bool thread_exited = false;
			bool child_exited = false;

			while (!stopped || !thread_created || !exited || !execd ||
			       !child_exited || !thread_exited) {
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
					    ((int) args.Data == bpt_main)) {
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

			AssertFrame (thread, "X.Main(string[])", line_main_3);
			AssertPrint (thread, "process.ExitCode", "(int) 0");
			AssertTargetOutput ("Hello World!");
			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Fork")]
		public void ManagedChild ()
		{
			Interpreter.Options.File = BuildDirectory + "/TestExec.exe";
			Interpreter.Options.InferiorArgs = new string [] {
				MonoExecutable, BuildDirectory + "/TestChild.exe"
			};

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			AssertExecute ("continue -wait");

			Thread child = AssertProcessCreated ();
			Thread execd_child = null;

			bool exited = false;
			bool execd = false;
			bool stopped = false;
			bool thread_created = false;
			bool child_exited = false;
			bool thread_exited = false;

			while (!stopped || !thread_created || !exited || !execd ||
			       !child_exited || !thread_exited) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.Threads, "EXEC EVENT: {0}", e);

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
					    ((int) args.Data == bpt_main)) {
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

			AssertFrame (thread, "X.Main(string[])", line_main_3);
			AssertPrint (thread, "process.ExitCode", "(int) 0");
			AssertTargetOutput ("Hello World");
			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
