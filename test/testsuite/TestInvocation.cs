using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestInvocation : DebuggerTestFixture
	{
		public TestInvocation ()
			: base ("TestInvocation")
		{ }

		int bpt_breakpoint_test;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, "test", "X.Test(int, string)");

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

			AssertExecute ("continue");

			AssertTargetOutput ("Foo: 5");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertTargetOutput ("Hello: 5");
			AssertTargetOutput ("Static Hello: 5");

			AssertHitBreakpoint (thread, "test static",
					     "X.TestStatic(X, int, string)");

			AssertPrint (thread, "a", "(int) 9");
			AssertPrint (thread, "b", "(string) \"Boston\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Foo: 9");
			AssertTargetOutput ("Foo with a string: Boston");
			AssertTargetOutput ("Hello: 9");
			AssertTargetOutput ("Static Hello: 9");
			AssertTargetOutput ("Static Hello: 9");

			AssertHitBreakpoint (thread, "main2", "X.Main()");

			bpt_breakpoint_test = AssertBreakpoint ("BreakpointTest");
			AssertExecute ("call BreakpointTest ()");

			AssertHitBreakpoint (thread, bpt_breakpoint_test,
					     "X.BreakpointTest()", GetLine ("breakpoint test"));

			AssertExecute ("continue");
			AssertRuntimeInvokeDone (thread, "X.Main()", GetLine ("main2"));
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_breakpoint_test,
					     "X.BreakpointTest()", GetLine ("breakpoint test"));

			AssertExecute ("continue");

			AssertTargetExited (thread.Process);

		}
	}
}
