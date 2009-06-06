using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestBreakpoint2 : TestSuite
	{
		public TestBreakpoint2 ()
			: base ("TestBreakpoint2")
		{ }

		public override void SetUp ()
		{
			base.SetUp ();
			AddSourceFile ("TestBreakpoint2-Module.cs");
		}

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("step");
			AssertStopped (thread, "run", "X.Run()");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "foo", "Foo.Run()");

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World!");
			AssertTargetExited (thread.Process);
		}
	}
}
