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
		{
			Config.BrokenThreading = false;
			Config.StayInThread = true;
		}

		public override void SetUp ()
		{
			base.SetUp ();
			Interpreter.IgnoreThreadCreation = true;
		}

		const int LineHelloWorld = 7;

		int bpt_world;

		[Test]
		[Category("AppDomain")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "main", "X.Main()");

			bpt_world = AssertBreakpoint (true, "Hello.World");

			AssertExecute ("next");
			AssertStopped (thread, "main+1", "X.Main()");

			AssertExecute ("next");
			AssertStopped (thread, "main2", "X.Main()");

			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_world, "Hello.World()", LineHelloWorld);
			AssertExecute ("continue");

			AssertTargetOutput ("Hello World from Test!");

			AssertHitBreakpoint (thread, "unload", "X.Main()");
			AssertExecute ("next -wait");

			AssertStopped (thread, "end", "X.Main()");

			AssertExecute ("continue");
			AssertTargetOutput ("UNLOADED!");
			AssertTargetExited (thread.Process);
		}
	}
}
