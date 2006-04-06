using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestInheritance : TestSuite
	{
		public TestInheritance ()
			: base ("../test/src/TestInheritance.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 145;
			const int line_main_2 = 157;
			const int line_hello = 95;

			AssertStopped (thread, "X.Main()", line_main);
			int bpt_main_2 = AssertBreakpoint (line_main_2);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			AssertPrint (thread, "d.Test ()", "(System.String) \"Hello World\"");
			AssertPrint (thread, "a.Test ()", "(System.String) \"Hello World\"");
			AssertPrint (thread, "a.ToString ()", "(System.String) \"5\"");
			AssertPrint (thread, "(A) a", "(A) { \"5\" }");
			AssertPrintException (thread, "(B) a", "Cannot cast from A to B.");
			AssertPrint (thread, "d", "(D) { e = 500 }");
			AssertPrint (thread, "(C) d", "(C) { f = 3.14 }");
			AssertPrint (thread, "(B) d", "(B) { a = 8, Hello = \"Hello World\" }");
			AssertPrintException (thread, "(A) d", "Cannot cast from D to A.");
			AssertPrint (thread, "(D) d", "(D) { e = 500 }");
			AssertPrint (thread, "((D) d)", "(D) { e = 500 }");
			AssertPrint (thread, "(((D) d))", "(D) { e = 500 }");
			AssertPrint (thread, "((B) d).Test ()", "(System.String) \"Hello World\"");
			AssertPrint (thread, "((C) d).Test ()", "(System.String) \"Hello World\"");
			AssertPrint (thread, "(((D) d).Test ())", "(System.String) \"Hello World\"");
			AssertPrintException (thread, "d.Hello ()",
					      "Method `d.Hello ()' doesn't return a value.");
			AssertPrint (thread, "d.Virtual ()", "(System.Int32) 2");
			AssertPrint (thread, "((C) d).Virtual ()", "(System.Int32) 2");
			AssertPrint (thread, "a.Hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "a.Property", "(System.String) \"Hello World\"");
			AssertPrint (thread, "A.StaticTest ()", "(System.String) \"Boston\"");
			AssertPrint (thread, "A.StaticProperty", "(System.String) \"Boston\"");
			AssertPrint (thread, "((B) d).Hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "((B) d).Property", "(System.String) \"Hello World\"");
			AssertPrint (thread, "B.StaticTest ()", "(System.String) \"Boston\"");
			AssertPrint (thread, "B.StaticProperty", "(System.String) \"Boston\"");
			AssertPrint (thread, "d.f", "(System.Single) 3.14");
			AssertPrint (thread, "d.a", "(System.Int32) 8");
			AssertPrint (thread, "c.Virtual ()", "(System.Int32) 2");
			AssertPrint (thread, "hello.Test ()", "(System.String) \"Hello\"");
			AssertPrint (thread, "world.Test ()", "(System.String) \"World\"");
			AssertPrint (thread, "((D) c)", "(D) { e = 500 }");
			AssertPrint (thread, "c.Test (c)", "(System.Int32) 64");

			int bpt_hello = AssertBreakpoint (line_hello);
			AssertExecute ("continue");
			AssertTargetOutput ("Hello");
			AssertTargetOutput ("World");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_hello, "C.Hello()", line_hello);

			AssertPrint (thread, "this", "(C) { f = 3.14 }");
			AssertPrint (thread, "f", "(System.Single) 3.14");
			AssertPrint (thread, "base.a", "(System.Int32) 8");
			AssertPrint (thread, "base.Hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "Virtual ()", "(System.Int32) 2");

			AssertExecute ("kill");
		}
	}
}
