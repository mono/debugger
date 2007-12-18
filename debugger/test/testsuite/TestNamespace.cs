using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestNamespace : TestSuite
	{
		public TestNamespace ()
			: base ("TestNamespace")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 36;
			const int line_main_2 = 37;
			const int line_world = 12;
			const int line_test = 46;

			AssertStopped (thread, "Test.X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2);
			int bpt_world = AssertBreakpoint (line_world);
			int bpt_test = AssertBreakpoint ("Y.Test");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_world, "Martin.Baulig.Hello.World()",
					     line_world);

			AssertPrint (thread, "Foo.Print ()", "(string) \"Boston\"");
			AssertPrintException (thread, "Foo.Print",
					      "Expression `Martin.Baulig.Foo.Print' is a method, " +
					      "not a field or property.");
			AssertPrint (thread, "Foo.Boston", "(string) \"Boston\"");
			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(string) \"Boston\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Boston");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_main_2, "Test.X.Main()", line_main_2);

			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(string) \"Boston\"");
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_test, "Y.Test()", line_test);
			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(string) \"Boston\"");
			AssertPrint (thread, "Martin.Baulig.Foo.Print ()", "(string) \"Boston\"");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
