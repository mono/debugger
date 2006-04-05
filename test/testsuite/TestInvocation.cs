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
			: base ("../test/src/TestInvocation.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 47);
			int bpt = AssertBreakpoint ("31");
			Execute ("continue");

			AssertHitBreakpoint (thread, bpt,
					     "X.Test(System.Int32,System.String)", 31);

			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertPrint (thread, "Hello (4)", "(System.Int32) 16");
			AssertTargetOutput ("Hello: 4");
			AssertPrint (thread, "Hello (a)", "(System.Int32) 20");
			AssertTargetOutput ("Hello: 5");
			AssertPrint (thread, "Foo (4)", "(System.Int32) 8");
			AssertTargetOutput ("Foo: 4");

			AssertPrint (thread, "Foo (\"Hello World\")",
				     "(System.String) \"Returned Hello World\"");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertPrint (thread, "Foo (b)",
				     "(System.String) \"Returned Hello World\"");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertPrint (thread, "StaticHello (4)", "(System.Int32) 32");
			AssertTargetOutput ("Static Hello: 4");
			AssertPrint (thread, "StaticHello (Hello (4))", "(System.Int32) 128");
			AssertTargetOutput ("Hello: 4");
			AssertTargetOutput ("Static Hello: 16");

			bpt = AssertBreakpoint ("39");
			Execute ("continue");

			AssertTargetOutput ("Foo: 5");
			AssertTargetOutput ("Foo with a string: Hello World");
			AssertTargetOutput ("Hello: 5");
			AssertTargetOutput ("Static Hello: 5");

			AssertHitBreakpoint (thread, bpt,
					     "X.TestStatic(X,System.Int32,System.String)", 39);

			AssertPrint (thread, "a", "(System.Int32) 9");
			AssertPrint (thread, "b", "(System.String) \"Boston\"");

			Execute ("continue");
			AssertTargetOutput ("Foo: 9");
			AssertTargetOutput ("Foo with a string: Boston");
			AssertTargetOutput ("Hello: 9");
			AssertTargetOutput ("Static Hello: 9");
			AssertTargetOutput ("Static Hello: 9");

			Execute ("kill");
		}
	}
}
