using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestInvocation : TestSuite
	{
		public TestInvocation ()
			: base ("TestInvocation")
		{ }

		const int LineTest = 31;
		const int LineTestStatic = 39;
		const int LineBreakpointTest = 46;
		const int LineMain = 50;
		const int LineMain2 = 54;

		int bpt_test;
		int bpt_test_static;
		int bpt_bpt_test;
		int bpt_main;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", LineMain);
			bpt_test = AssertBreakpoint (LineTest);
			bpt_main = AssertBreakpoint (LineMain2);
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_test, "X.Test(int,string)", LineTest);

			AssertPrint (thread, "a", "(int) 5");
			AssertPrint (thread, "Hello (4)", "(int) 16");
			AssertTargetOutput ("Hello: 4");
			AssertPrint (thread, "Hello (a)", "(int) 20");
			AssertTargetOutput ("Hello: 5");
			AssertPrint (thread, "Foo (4)", "(int) 8");
			AssertTargetOutput ("Foo: 4");

			AssertPrint (thread, "Foo (\"Hello World\")",
				     "(string) \"Returned Hello World\"");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertPrint (thread, "Foo (b)",
				     "(string) \"Returned Hello World\"");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertPrint (thread, "StaticHello (4)", "(int) 32");
			AssertTargetOutput ("Static Hello: 4");
			AssertPrint (thread, "StaticHello (Hello (4))", "(int) 128");
			AssertTargetOutput ("Hello: 4");
			AssertTargetOutput ("Static Hello: 16");

			bpt_test_static = AssertBreakpoint (LineTestStatic);
			AssertExecute ("continue");

			AssertTargetOutput ("Foo: 5");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertTargetOutput ("Hello: 5");
			AssertTargetOutput ("Static Hello: 5");

			AssertHitBreakpoint (thread, bpt_test_static,
					     "X.TestStatic(X,int,string)", LineTestStatic);

			AssertPrint (thread, "a", "(int) 9");
			AssertPrint (thread, "b", "(string) \"Boston\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Foo: 9");
			AssertTargetOutput ("Foo with a string: Boston");
			AssertTargetOutput ("Hello: 9");
			AssertTargetOutput ("Static Hello: 9");
			AssertTargetOutput ("Static Hello: 9");

			AssertHitBreakpoint (thread, bpt_main,
					     "X.Main()", LineMain2);

			bpt_bpt_test = AssertBreakpoint ("BreakpointTest");
			AssertExecute ("call BreakpointTest ()");

			AssertHitBreakpoint (thread, bpt_bpt_test,
					     "X.BreakpointTest()", LineBreakpointTest);

			AssertExecute ("continue");
			AssertStopped (thread, "X.Main()", LineMain2);
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_bpt_test,
					     "X.BreakpointTest()", LineBreakpointTest);

			AssertExecute ("continue");

			AssertTargetExited (thread.Process);

		}
	}
}
