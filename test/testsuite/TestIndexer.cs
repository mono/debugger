using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestIndexer : TestSuite
	{
		public TestIndexer ()
			: base ("TestIndexer")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 30);

			int bpt = AssertBreakpoint ("32");
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt, "X.Main()", 32);

			AssertPrint (thread, "x[0]", "(System.String) \"Hello\"");
			AssertPrint (thread, "x[1]", "(System.String) \"World\"");
			AssertPrint (thread, "x[0,\"Berlin\"]", "(System.String) \"Hello Berlin\"");
			AssertExecute ("set x[0] = \"Test\"");
			AssertPrint (thread, "x[0]", "(System.String) \"Test\"");

			AssertExecuteException ("set x[0,\"Berlin\"] = \"Trier\"",
						"No overload of method `X.x[]' has 3 arguments.");

			AssertExecute ("kill");
		}
	}
}
