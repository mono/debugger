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
			: base ("../test/src/TestNamespace.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 36;
			const int line_main_2 = 37;
			const int line_world = 12;
			const int line_test = 46;

			AssertStopped (thread, "Test.X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2.ToString ());
			int bpt_world = AssertBreakpoint (line_world.ToString ());
			int bpt_test = AssertBreakpoint ("Y.Test");

			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_world, "Martin.Baulig.Hello.World()",
					     line_world);

			AssertPrint (thread, "Foo.Print ()", "(System.String) \"Boston\"");
			AssertPrintException (thread, "Foo.Print",
					      "Expression `Martin.Baulig.Foo.Print' is a method, " +
					      "not a field or property.");
			AssertPrint (thread, "Foo.Boston", "(System.String) \"Boston\"");
			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(System.String) \"Boston\"");

			Execute ("continue");
			AssertTargetOutput ("Boston");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_main_2, "Test.X.Main()", line_main_2);

			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(System.String) \"Boston\"");
			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_test, "Y.Test()", line_test);
			AssertPrint (thread, "Martin.Baulig.Foo.Boston", "(System.String) \"Boston\"");
			AssertPrint (thread, "Martin.Baulig.Foo.Print ()", "(System.String) \"Boston\"");

			Execute ("kill");
		}
	}
}
