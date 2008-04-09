using System;
using System.Collections.Generic;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestIterator : TestSuite
	{
		public TestIterator ()
			: base ("TestIterator")
		{ }

		[Test]
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

			AssertHitBreakpoint (thread, "test1 run", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 loop", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 yield1", "Test1.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 statement", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 loop", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 lexical", "Test1.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 yield2", "Test1.X.GetRange()");

			AssertPrint (thread, "a", "(int) 3");

			AssertExecute ("step");
			AssertStopped (thread, "test1 statement", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 loop", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 yield3", "Test1.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 statement", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 loop", "Test1.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test1 return", "Test1.X.Run()");

			AssertExecute ("continue");

			//
			// Test2
			//

			AssertHitBreakpoint (thread, "test2 run", "Test2.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 loop", "Test2.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 iterator loop", "Test2.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 iterator if", "Test2.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 iterator yield", "Test2.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 statement", "Test2.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 stop", "Test2.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 loop", "Test2.X.Run()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 iterator loop", "Test2.X.GetRange()");

			AssertExecute ("step");
			AssertStopped (thread, "test2 iterator if", "Test2.X.GetRange()");

			AssertPrint (thread, "total", "(int) 2");
			AssertPrint (thread, "stop", "(bool) true");
			AssertPrint (thread, "this", "(Test2.X) { total = 2, stop = true }");

			AssertExecute ("step");
			AssertStopped (thread, "test2 return", "Test2.X.Run()");

			AssertExecute ("continue");

			//
			// Done
			//

			AssertTargetExited (thread.Process);
		}
	}
}
