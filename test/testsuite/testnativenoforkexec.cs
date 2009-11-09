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
	public class testnativenoforkexec : DebuggerTestFixture
	{
		public testnativenoforkexec ()
			: base ("testnativenoforkexec", "testnativenoforkexec.c")
		{ }

		const int LineMain = 12;
		const int LineChild = 7;

		const int LineManagedMain = 8;
		const int LineManagedBpt = 9;

		int bpt_child;
		int bpt_managed_child;

		public override void SetUp ()
		{
			base.SetUp ();
			Config.FollowFork = true;
			Config.ThreadingModel = ThreadingModel.Single;

			string child_file = Path.GetFullPath (Path.Combine (SourceDirectory, "testnativechild.c"));
			bpt_child = AssertBreakpoint (String.Format ("-global -lazy {0}:{1}", child_file, LineChild + 1));

			string mchild_file = Path.GetFullPath (Path.Combine (SourceDirectory, "TestChild.cs"));
			bpt_managed_child = AssertBreakpoint (String.Format ("-global -lazy {0}:{1}", mchild_file, LineManagedBpt));
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void Main ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "testnativenoforkexec");
			Interpreter.Options.InferiorArgs = new string [] {
				Path.Combine (BuildDirectory, "testnativechild")
			};

			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("disable " + bpt_child);

			AssertExecute ("continue");

			bool thread_created = false;
			bool execd = false;
			bool stopped = false;

			Thread execd_child = null;

			while (!execd || !thread_created || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExecd) {
					execd = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						AssertFrame (execd_child, "main", LineChild);
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			bool exited_event = false;
			bool process_exited = false;
			bool thread_exited = false;
			bool exited = false;

			while (!exited || !exited_event || !thread_exited || !process_exited) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ThreadExited) {
					if ((Thread) e.Data == execd_child) {
						thread_exited = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.ProcessExited) {
					process_exited = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetExited)) {
						exited_event = true;
						continue;
					}
				} else if (e.Type == DebuggerEventType.TargetExited) {
					exited = true;
					continue;
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertTargetOutput ("Hello World!");
			AssertNoTargetOutput ();
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void Breakpoint ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "testnativenoforkexec");
			Interpreter.Options.InferiorArgs = new string [] {
				Path.Combine (BuildDirectory, "testnativechild")
			};

			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("enable " + bpt_child);
			AssertExecute ("continue -wait");

			bool thread_created = false;
			bool stopped = false;
			bool execd = false;

			Thread execd_child = null;

			while (!execd || !thread_created || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExecd) {
					execd = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						AssertFrame (execd_child, "main", LineChild);
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertHitBreakpoint (execd_child, bpt_child, "main", LineChild+1);

			AssertExecute ("continue");

			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void ThreadingModelProcess ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "testnativenoforkexec");
			Interpreter.Options.InferiorArgs = new string [] {
				Path.Combine (BuildDirectory, "testnativechild")
			};

			Config.ThreadingModel = ThreadingModel.Process;

			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("enable " + bpt_child);
			AssertExecute ("continue -wait");

			bool thread_created = false;
			bool stopped = false;
			bool execd = false;

			Thread execd_child = null;

			while (!execd || !thread_created || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExecd) {
					execd = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						AssertFrame (execd_child, "main", LineChild);
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertHitBreakpoint (execd_child, bpt_child, "main", LineChild+1);

			AssertExecute ("continue");

			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void ThreadingModelGlobal ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "testnativenoforkexec");
			Interpreter.Options.InferiorArgs = new string [] {
				Path.Combine (BuildDirectory, "testnativechild")
			};

			Config.ThreadingModel = ThreadingModel.Global;

			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("enable " + bpt_child);
			AssertExecute ("continue -wait");

			bool thread_created = false;
			bool stopped = false;
			bool execd = false;

			Thread execd_child = null;

			while (!execd || !thread_created || !stopped) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExecd) {
					execd = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						AssertFrame (execd_child, "main", LineChild);
						stopped = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertHitBreakpoint (execd_child, bpt_child, "main", LineChild+1);

			AssertExecute ("continue");

			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void ManagedChild ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "testnativenoforkexec");
			Interpreter.Options.InferiorArgs = new string [] {
				MonoExecutable, Path.Combine (BuildDirectory, "TestChild.exe")
			};

			Config.ThreadingModel = ThreadingModel.Global;
			AssertExecute ("disable " + bpt_child);
			AssertExecute ("enable " + bpt_managed_child);

			Process process = Start ();
			Assert.IsFalse (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);
			AssertExecute ("continue -wait");

			bool thread_created = false;
			bool reached_main = false;
			bool execd = false;

			Thread execd_child = null;

			while (!execd || !thread_created || !reached_main) {
				DebuggerEvent e = AssertEvent ();

				if (e.Type == DebuggerEventType.ProcessExecd) {
					execd = true;
					continue;
				} else if (e.Type == DebuggerEventType.ThreadCreated) {
					execd_child = (Thread) e.Data;
					thread_created = true;
					continue;
				} else if (e.Type == DebuggerEventType.TargetEvent) {
					Thread e_thread = (Thread) e.Data;
					TargetEventArgs args = (TargetEventArgs) e.Data2;

					if ((e_thread == execd_child) &&
					    (args.Type == TargetEventType.TargetStopped)) {
						AssertFrame (execd_child, "X.Main()", LineManagedMain);
						reached_main = true;
						continue;
					}
				}

				Assert.Fail ("Received unexpected event {0}", e);
			}

			AssertExecute ("continue");

			AssertHitBreakpoint (execd_child, bpt_managed_child, "X.Main()", LineManagedBpt);

			AssertExecute ("continue");

			AssertTargetOutput ("Hello World");
			AssertTargetExited (thread.Process);
		}

	}
}
