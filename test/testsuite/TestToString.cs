using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestToString : DebuggerTestFixture
	{
		public TestToString ()
			: base ("TestToString")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 80;
			const int line_main_2 = 85;
			const int line_main_3 = 87;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			// AssertExecute ("bt");

			AssertPrint (thread, "foo.ToString()", "(string) \"Hello World!\"");
			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", line_main_3);

			// AssertExecute ("bt");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
