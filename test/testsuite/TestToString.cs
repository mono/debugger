using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestToString : TestSuite
	{
		public TestToString ()
			: base ("../test/src/TestToString.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 80;
			const int line_main_2 = 85;
			const int line_main_3 = 87;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2.ToString ());
			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			// Execute ("bt");

			AssertPrint (thread, "foo.ToString()", "(System.String) \"Hello World!\"");
			Execute ("next");
			AssertStopped (thread, "X.Main()", line_main_3);

			// Execute ("bt");

			Execute ("kill");
		}
	}
}
