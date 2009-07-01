using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestInheritance : DebuggerTestFixture
	{
		public TestInheritance ()
			: base ("TestInheritance")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
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

			AssertPrint (thread, "d.Test ()", "(string) \"Hello World\"");
			AssertPrint (thread, "a.Test ()", "(string) \"Hello World\"");
			AssertPrint (thread, "a.ToString ()", "(string) \"5\"");
			AssertPrint (thread, "(A) a", "(A) { \"5\" }");
			AssertPrintException (thread, "(B) a", "Cannot cast from `a' to `B'.");
			AssertPrint (thread, "d", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "(C) d",
				     "(C) { <B> = { a = 8, Hello = \"Hello World\" }, f = 3.14 }");
			AssertPrint (thread, "(B) d", "(B) { a = 8, Hello = \"Hello World\" }");
			AssertPrintException (thread, "(A) d", "Cannot cast from `d' to `A'.");
			AssertPrint (thread, "(D) d", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "((D) d)", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "(((D) d))", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "((B) d).Test ()", "(string) \"Hello World\"");
			AssertPrint (thread, "((C) d).Test ()", "(string) \"Hello World\"");
			AssertPrint (thread, "(((D) d).Test ())", "(string) \"Hello World\"");
			AssertPrintException (thread, "d.Hello ()",
					      "Method `d.Hello ()' doesn't return a value.");
			AssertPrint (thread, "d.Virtual ()", "(int) 2");
			AssertPrint (thread, "((C) d).Virtual ()", "(int) 2");
			AssertPrint (thread, "a.Hello", "(string) \"Hello World\"");
			AssertPrint (thread, "a.Property", "(string) \"Hello World\"");
			AssertPrint (thread, "A.StaticTest ()", "(string) \"Boston\"");
			AssertPrint (thread, "A.StaticProperty", "(string) \"Boston\"");
			AssertPrint (thread, "((B) d).Hello", "(string) \"Hello World\"");
			AssertPrint (thread, "((B) d).Property", "(string) \"Hello World\"");
			AssertPrint (thread, "B.StaticTest ()", "(string) \"Boston\"");
			AssertPrint (thread, "B.StaticProperty", "(string) \"Boston\"");
			AssertPrint (thread, "d.f", "(float) 3.14");
			AssertPrint (thread, "d.a", "(int) 8");
			AssertPrint (thread, "c.Virtual ()", "(int) 2");
			AssertPrint (thread, "hello.Test ()", "(string) \"Hello\"");
			AssertPrint (thread, "world.Test ()", "(string) \"World\"");
			AssertPrint (thread, "((D) c)", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "c.Test (c)", "(int) 64");

			int bpt_hello = AssertBreakpoint (line_hello);
			AssertExecute ("continue");
			AssertTargetOutput ("Hello");
			AssertTargetOutput ("World");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_hello, "C.Hello()", line_hello);

			AssertPrint (thread, "this", "(D) { <C> = { <B> = { a = 8, " +
				     "Hello = \"Hello World\" }, f = 3.14 }, e = 500 }");
			AssertPrint (thread, "f", "(float) 3.14");
			AssertPrint (thread, "base.a", "(int) 8");
			AssertPrint (thread, "base.Hello", "(string) \"Hello World\"");
			AssertPrint (thread, "Virtual ()", "(int) 2");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
