using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestByRef : TestSuite
	{
		public TestByRef ()
			: base ("../test/src/TestByRef.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 20;
			const int line_test = 7;
			const int line_unsafe = 14;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_test = AssertBreakpoint ("Test");
			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_test, "X.Test(System.Int32&)", line_test);

			Execute ("step");
			AssertStopped (thread, "X.Test(System.Int32&)", line_test + 1);

			AssertPrint (thread, "foo", "(System.Int32&) &(System.Int32) 3");
			int bpt_unsafe = AssertBreakpoint (line_unsafe);

			Execute ("continue");
			AssertTargetOutput ("3");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_unsafe, "X.UnsafeTest(System.Int32)",
					     line_unsafe);

			AssertPrint (thread, "ptr", "(System.Int32&) &(System.Int32) 3");
			AssertPrint (thread, "*ptr", "(System.Int32) 3");

			Execute ("kill");
		}
	}
}
