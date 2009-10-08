using System;
using System.IO;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture(Timeout = 15000)]
	public class TestExec : DebuggerTestFixture
	{
		public TestExec ()
			: base ("TestExec.exe", "TestExec.cs",
				BuildDirectory + "/testnativechild")
		{ }

		const int line_main = 8;
		const int line_main_3 = 12;
		const int line_child = 9;

		int bpt_main;
		int bpt_child;

		public override void SetUp ()
		{
			base.SetUp ();
			Config.FollowFork = true;
			Config.ThreadingModel = ThreadingModel.Single;

			bpt_main = AssertBreakpoint (String.Format ("-local {0}:{1}", FileName, line_main_3));

			string child_file = Path.GetFullPath (Path.Combine (SourceDirectory, "TestChild.cs"));
			bpt_child = AssertBreakpoint (String.Format ("-global -lazy {0}:{1}", child_file, line_child));
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void NativeChild ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "TestExec.exe");
			Interpreter.Options.InferiorArgs = new string [] {
				Path.Combine (BuildDirectory, "testnativechild")
			};

			AssertExecute ("disable " + bpt_child);

			Interpreter.DebuggerConfiguration.ThreadingModel = ThreadingModel.Single;

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
			bool thread_exited = false;
			bool child_exited = false;

			while (!stopped || !thread_created || !exited || !execd ||
			       !child_exited || !thread_exited) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

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
			Interpreter.Options.File = Path.Combine (BuildDirectory, "TestExec.exe");
			Interpreter.Options.InferiorArgs = new string [] {
				MonoExecutable, Path.Combine (BuildDirectory, "TestChild.exe")
			};

			Interpreter.DebuggerConfiguration.ThreadingModel = ThreadingModel.Single;

			AssertExecute ("disable " + bpt_child);

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

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

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

		[Test]
		[Category("Fork")]
		public void ThreadingModelProcess ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "TestExec.exe");
			Interpreter.Options.InferiorArgs = new string [] {
				MonoExecutable, Path.Combine (BuildDirectory, "TestChild.exe")
			};

			AssertExecute ("enable " + bpt_child);

			Interpreter.DebuggerConfiguration.ThreadingModel = ThreadingModel.Process;

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			AssertExecute ("continue -bg");

			Thread child = AssertProcessCreated ();
			Thread execd_child = null;

			bool execd = false;
			bool stopped = false;
			bool thread_created = false;

			while (!stopped || !execd || !thread_created) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

				if (e.Type == DebuggerEventType.ProcessExecd) {
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

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_child)) {
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			Assert.IsTrue (thread.IsRunning);
			Assert.IsTrue (execd_child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == execd_child);

			AssertFrame (execd_child, "X.Main()", line_child);

			AssertExecute ("continue -thread " + execd_child.ID);

			bool exited_event = false;
			bool thread_exited = false;
			bool child_exited = false;
			bool reached_waitpid = false;

			while (!child_exited || !thread_exited || !reached_waitpid || !exited_event) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

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
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_main)) {
						reached_waitpid = true;
						continue;
					} else if ((e_thread == execd_child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited_event = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertFrame (thread, "X.Main(string[])", line_main_3);
			AssertPrint (thread, "process.ExitCode", "(int) 0");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Fork")]
		public void ThreadingModelGlobal ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "TestExec.exe");
			Interpreter.Options.InferiorArgs = new string [] {
				MonoExecutable, Path.Combine (BuildDirectory, "TestChild.exe")
			};

			AssertExecute ("enable " + bpt_child);

			Interpreter.DebuggerConfiguration.ThreadingModel = ThreadingModel.Global;

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			AssertExecute ("continue");

			Thread child = AssertProcessCreated ();
			Thread execd_child = null;

			bool execd = false;
			bool stopped = false;
			bool thread_created = false;

			while (!stopped || !execd || !thread_created) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

				if (e.Type == DebuggerEventType.ProcessExecd) {
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

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_child)) {
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			Assert.IsTrue (thread.IsStopped);
			Assert.IsTrue (execd_child.IsStopped);
			Assert.IsTrue (Interpreter.CurrentThread == execd_child);

			AssertFrame (execd_child, "X.Main()", line_child);

			AssertExecute ("continue -thread " + thread.ID);

			bool exited_event = false;
			bool thread_exited = false;
			bool child_exited = false;
			bool reached_waitpid = false;

			while (!child_exited || !thread_exited || !reached_waitpid || !exited_event) {
				DebuggerEvent e = AssertEvent ();

				Report.Debug (DebugFlags.NUnit, "EXEC EVENT: {0}", e);

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
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == thread) &&
					    (args.Type == TargetEventType.TargetHitBreakpoint) &&
					    ((int) args.Data == bpt_main)) {
						reached_waitpid = true;
						continue;
					} else if ((e_thread == execd_child) &&
						   (args.Type == TargetEventType.TargetExited)) {
						exited_event = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertFrame (thread, "X.Main(string[])", line_main_3);
			AssertPrint (thread, "process.ExitCode", "(int) 0");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
