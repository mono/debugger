using System;
using System.Collections.Generic;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAnonymous : TestSuite
	{
		public TestAnonymous ()
			: base ("TestAnonymous")
		{ }

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "RunTests.Main()", GetLine ("main"));
			AssertExecute ("continue");

			//
			// Test1
			//

			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1(R,int)");

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, "test1 after foo", "Test1.X.Test1(R,int)");

			AssertExecute ("step");
			// FIXME: Print a better method name
			AssertStopped (thread, "Test1.X/<>c__CompilerGenerated5`1<R>.<Test1>c__6()",
				       GetLine ("test1 foo"));

			AssertPrint (thread, "a", "(int) 2");
			AssertPrint (thread, "b", "(int) 2");
			AssertPrint (thread, "r", "(long) 500");
			AssertPrint (thread, "s", "(long) 500");

			AssertExecute ("continue");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertTargetOutput ("2");
			AssertTargetOutput ("500");
			AssertHitBreakpoint (thread, "test1", "Test1.X.Test1(R,int)");

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

			AssertHitBreakpoint (thread, "test2", "Test2.X.Test(T)");
			AssertPrint (thread, "t", "(int) 3");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "t", "int");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 after foo", "Test2.X.Test(T)");

			AssertExecute ("step");
			AssertStopped (thread,
				       "Test2.X/<>c__CompilerGenerated1`1<T>.<Test>c__7()",
				       GetLine ("test2 foo"));

			// FIXME
			// AssertPrint (thread, "t", "(int) 3");
			// AssertType (thread, "t", "int");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test2 hello", "Test2.X.Hello(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");

			//
			// Test3
			//

			AssertHitBreakpoint (thread, "test3", "Test3.X.Test(T)");
			AssertPrint (thread, "t", "(int) 3");
			AssertType (thread, "t", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 hello", "Test3.X.Hello(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 after foo", "Test3.X.Test(T)");

			AssertExecute ("step");
			AssertStopped (thread,
				       "Test3.X/<>c__CompilerGenerated2`1<T>.<Test>c__8(S)",
				       GetLine ("test3 foo"));

			// FIXME
			// AssertPrint (thread, "t", "(int) 3");
			// AssertType (thread, "t", "int");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test3 hello", "Test3.X.Hello(U)");
			AssertPrint (thread, "u", "(int) 3");
			AssertType (thread, "u", "int");

			AssertExecute ("continue");

			//
			// Test4
			//

			AssertHitBreakpoint (thread, "test4", "Test4.Test`1<T>.Hello(T,S)");
			AssertPrint (thread, "t", "(string) \"Kahalo\"");
			AssertType (thread, "t", "string");
			AssertPrint (thread, "s", String.Format ("(double) {0}", System.Math.PI));
			AssertType (thread, "s", "double");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
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
