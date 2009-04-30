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

		public override void SetUp ()
		{
			base.SetUp ();
			Interpreter.IgnoreThreadCreation = true;
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

			AssertStopped (thread, "X.Main()", GetLine ("unload") + 1);

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

			AssertStopped (thread, "X.Main()", GetLine ("unload") + 1);

			AssertExecute ("continue");

			AssertTargetExited (thread.Process);
		}

	}
}
