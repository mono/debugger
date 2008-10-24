using System;
using System.IO;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestDontFollowFork : TestSuite
	{
		public TestDontFollowFork ()
			: base ("TestExec.exe", "TestExec.cs")
		{ }

		const int line_main = 8;
		const int line_main_3 = 12;

		int bpt_main;

		public override void SetUp ()
		{
			base.SetUp ();
			Config.FollowFork = false;
		}

		[Test]
		[Category("Native")]
		[Category("Fork")]
		public void NativeChild ()
		{
			Interpreter.Options.File = Path.Combine (BuildDirectory, "TestExec.exe");
			Interpreter.Options.InferiorArgs = new string [] { Path.Combine (BuildDirectory, "testnativechild") };

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			bpt_main = AssertBreakpoint ("-local " + line_main_3);
			AssertExecute ("continue -wait");

			AssertHitBreakpoint (thread, bpt_main,"X.Main(string[])", line_main_3);

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

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main(string[])", line_main);
			if (bpt_main == 0)
				bpt_main = AssertBreakpoint ("-local " + line_main_3);
			AssertExecute ("continue -wait");

			AssertHitBreakpoint (thread, bpt_main,"X.Main(string[])", line_main_3)
;
			AssertPrint (thread, "process.ExitCode", "(int) 0");
			AssertTargetOutput ("Hello World");
			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
