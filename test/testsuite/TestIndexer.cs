using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestIndexer : DebuggerTestFixture
	{
		public TestIndexer ()
			: base ("TestIndexer")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 30);

			int bpt = AssertBreakpoint ("32");
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt, "X.Main()", 32);

			AssertPrint (thread, "x[0]", "(string) \"Hello\"");
			AssertPrint (thread, "x[1]", "(string) \"World\"");
			AssertPrint (thread, "x[0,\"Berlin\"]", "(string) \"Hello Berlin\"");
			AssertExecute ("set x[0] = \"Test\"");
			AssertPrint (thread, "x[0]", "(string) \"Test\"");

			AssertExecuteException ("set x[0,\"Berlin\"] = \"Trier\"",
						"No overload of method `X.x[]' has 3 arguments.");

			AssertExecute ("continue");
			AssertTargetOutput ("Test");
			AssertTargetExited (thread.Process);
		}
	}
}
