using System;
using System.IO;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestSession : TestSuite
	{
		public TestSession ()
			: base ("TestSession")
		{ }

		const int line_main = 7;
		const int line_test = 15;
		const int line_bar = 23;

		int bpt_test;
		int bpt_bar;

		byte[] session;

		[Test]
		[Category("Session")]
		public void Main ()
		{
			Compile (FileName);

			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			bpt_test = AssertBreakpoint (line_test);
			bpt_bar = AssertBreakpoint ("Foo.Bar");

			using (MemoryStream ms = new MemoryStream ()) {
				Interpreter.SaveSession (ms);
				session = ms.GetBuffer ();
			}
			AssertExecute ("kill");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Session")]
		public void Load ()
		{
			Compile (FileName);

			Process process;
			using (MemoryStream ms = new MemoryStream (session))
				process = Interpreter.LoadSession (ms);
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Session")]
		public void LoadAgain ()
		{
			Compile (FileName);

			Process process;
			using (MemoryStream ms = new MemoryStream (session))
				process = Interpreter.LoadSession (ms);
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetExited (thread.Process);
		}
	}
}
