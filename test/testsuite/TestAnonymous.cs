using System;
using System.Collections.Generic;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestAnonymous : DebuggerTestFixture
	{
		public TestAnonymous ()
			: base ("TestAnonymous")
		{ }

		[Test]
		[Category("NotWorking")]
		[Category("Anonymous")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "RunTests.Main()");
			AssertExecute ("continue");

			//
			// Test1
			//

			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1<R>(R, int)");

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, "test1 after foo", "Test1.X.Test1<R>(R, int)");

			AssertExecute ("step");
			AssertStopped (thread, "test1 foo", "Test1.X.Test1<R>(R, int)~Test1.Foo()");

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "r", "(long) 500");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1<R>(R, int)");

			AssertPrint (thread, "b", "(int) 1");

			AssertExecute ("disable " + GetBreakpoint ("test1"));
			AssertExecute ("disable " + GetBreakpoint ("test1 after foo"));

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertTargetOutput ("1");
			AssertTargetOutput ("500");
			AssertTargetOutput ("-1");
			AssertTargetOutput ("500");

			//
			// Test2
			//

			AssertHitBreakpoint (thread, "test2", "Test2.X.Test<T>(T)");
			AssertPrint (thread, "t", "(int) 3");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "t", "int");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 after foo", "Test2.X.Test<T>(T)");

			AssertExecute ("step");
			AssertStopped (thread, "test2 foo", "Test2.X.Test<T>(T)~Test2.Foo()");

			AssertPrint (thread, "t", "(int) 3");
			AssertType (thread, "t", "int");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");

			//
			// Test3
			//

			AssertHitBreakpoint (thread, "test3", "Test3.X.Test<T>(T)");
			AssertPrint (thread, "t", "(int) 3");
			AssertType (thread, "t", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 hello", "Test3.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 after foo", "Test3.X.Test<T>(T)");

			AssertExecute ("step");
			AssertStopped (thread, "test3 foo", "Test3.X.Test<T>(T)~Test3.Foo<T>(T)");

			AssertPrint (thread, "t", "(int) 3");
			AssertType (thread, "t", "int");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 hello", "Test3.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 hello", "Test3.X.Hello<U>(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");

			//
			// Test4
			//

			AssertHitBreakpoint (thread, "test4", "Test4.Test<T>.Hello<S>(T, S)");
			AssertPrint (thread, "t", "(string) \"Kahalo\"");
			AssertType (thread, "t", "string");
			AssertPrint (thread, "s", String.Format ("(double) {0}", System.Math.PI));
			AssertType (thread, "s", "double");

			AssertExecute ("step");
			AssertStopped (thread, "test4 foo",
				       "Test4.Test<T>.Hello<S>(T, S)~Test4.Foo<long>(long)");

			AssertPrint (thread, "r", "(long) 5");
			AssertType (thread, "r", "long");
			AssertPrint (thread, "t", "(string) \"Kahalo\"");
			AssertType (thread, "t", "string");
			AssertPrint (thread, "s", String.Format ("(double) {0}", System.Math.PI));
			AssertType (thread, "s", "double");

			AssertExecute ("next");
			AssertTargetOutput ("5");
			AssertStopped (thread, "test4 foo2",
				       "Test4.Test<T>.Hello<S>(T, S)~Test4.Foo<long>(long)");

			AssertExecute ("next");
			AssertStopped (thread, "test4 foo3",
				       "Test4.Test<T>.Hello<S>(T, S)~Test4.Foo<long>(long)");

			AssertExecute ("step");
			AssertStopped (thread, "test4 bar",
				       "Test4.Test<T>.Hello<S>(T, S)~Test4.Bar<T>(T)");

			AssertPrint (thread, "r", "(long) 5");
			AssertType (thread, "r", "long");
			AssertPrint (thread, "x", "(string) \"Kahalo\"");
			AssertType (thread, "x", "string");
			AssertPrint (thread, "t", "(string) \"Kahalo\"");
			AssertType (thread, "t", "string");
			AssertPrint (thread, "s", String.Format ("(double) {0}", System.Math.PI));
			AssertType (thread, "s", "double");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertTargetOutput ("Kahalo");
			AssertTargetOutput (System.Math.PI.ToString ());
			AssertTargetOutput ("Kahalo");

			//
			// Done
			//

			AssertTargetExited (thread.Process);
		}
	}
}
