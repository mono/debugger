using System;
using System.IO;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestException : TestSuite
	{
		public TestException ()
			: base ("../test/src/TestException.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			string filename = Path.GetFullPath ("../test/src/TestException.cs");

			const int line_exception = 7;
			const int line_main = 12;
			const int line_main_2 = 19;

			AssertStopped (thread, "X.Main()", line_main);
			AssertCatchpoint ("InvalidOperationException");
			int bpt_main_2 = AssertBreakpoint (line_main_2);
			AssertExecute ("continue");

			AssertCaughtException (thread, "X.Test()", line_exception);
			AssertNoTargetOutput ();
			AssertExecute ("continue");
			AssertTargetOutput ("EXCEPTION: System.InvalidOperationException");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);
			AssertPrintException (thread, "x.Test()",
					      "Invocation of `x.Test ()' raised an exception: " +
					      "System.InvalidOperationException: Operation is not " +
					      "valid due to the current state of the object\n" +
					      "in [0x00005] (at " + filename + ":8) X:Test ()");
		}
	}
}
