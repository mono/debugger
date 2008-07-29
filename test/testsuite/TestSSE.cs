using System;
using System.Collections.Generic;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestSSE : TestSuite
	{
		public TestSSE ()
			: base ("TestSSE")
		{ }

		[Test]
		[Category("SSE")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "RunTests.Main()", GetLine ("main"));
			AssertExecute ("continue");

			//
			// While loop
			//

			AssertHitBreakpoint (thread, "while run", "WhileLoop.Run()");
			AssertExecute ("step");

			AssertStopped (thread, "WhileLoop.Run()", GetLine ("while loop"));
			AssertExecute ("step");
			AssertStopped (thread, "WhileLoop.Test()", GetLine ("while test"));
			AssertExecute ("step");
			AssertStopped (thread, "WhileLoop.Run()", GetLine ("while statement"));
			AssertExecute ("step");
			AssertStopped (thread, "WhileLoop.get_Total()", GetLine ("while total"));
			AssertExecute ("step");
			AssertStopped (thread, "WhileLoop.Run()", GetLine ("while loop"));
			AssertExecute ("next");
			AssertStopped (thread, "WhileLoop.Run()", GetLine ("while statement"));
			AssertExecute ("next");
			AssertStopped (thread, "WhileLoop.Run()", GetLine ("while loop"));

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "while return", "WhileLoop.Run()");

			AssertExecute ("continue");

			//
			// Foreach loop
			//

			AssertHitBreakpoint (thread, "foreach run", "Foreach.Run()");
			AssertExecute ("step");

			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach loop"));
			AssertExecute ("step");
			AssertStopped (thread, "Foreach.get_Values()", GetLine ("foreach values"));

// FIXME: broken
#if MARTIN_PRIVATE
			AssertExecute ("next");
			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach statement"));
			AssertExecute ("step");
			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach loop"));
			AssertExecute ("step");
			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach statement"));
			AssertExecute ("next");
			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach loop"));
			AssertExecute ("next");
			AssertStopped (thread, "Foreach.Run()", GetLine ("foreach statement"));
#endif

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "foreach return", "Foreach.Run()");

			//
			// Done
			//

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
