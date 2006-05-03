using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestProperty : TestSuite
	{
		public TestProperty ()
			: base ("TestProperty")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 43;
			const int line_main_2 = 44;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			AssertPrint (thread, "test.A", "(A) { }");
			AssertPrint (thread, "test.B", "(B[]) [ { } ]");
			AssertPrint (thread, "test.C", "(C[,]) [  ]");
			AssertPrint (thread, "test.Hello (new D ())", "(System.String) \"D\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Test");
			AssertTargetExited (thread.Process);
		}
	}
}
