using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAbort : TestSuite
	{
		public TestAbort ()
			: base ("TestAbort")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 33;
			const int line_hello = 13;
			const int line_hello_2 = 20;
			const int line_hello_3 = 22;
			const int line_hello_4 = 23;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("call Hello()");
			AssertStopped (thread, "X.Hello()", line_hello);

			Backtrace bt = thread.GetBacktrace (-1);
			if (bt.Count != 3)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 3);

			AssertFrame (bt [0], 0, "X.Hello()", line_hello);
			AssertInternalFrame (bt [1], 1);
			AssertFrame (bt [2], 2, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("call Hello()");
			AssertStopped (thread, "X.Hello()", line_hello);

			AssertExecute ("return -yes");
			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("call Hello (8)");
			AssertStopped (thread, "X.Hello(System.Int32)", line_hello_2);
			AssertExecute ("return -yes");
			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("call Hello (9)");
			AssertStopped (thread, "X.Hello(System.Int32)", line_hello_2);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello(System.Int32)", line_hello_3);
			AssertExecute ("return -yes");
			AssertTargetOutput ("Done: 9 18 1");
			AssertNoTargetOutput ();
			AssertStopped (thread, "X.Main()", line_main);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 1)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 1);

			AssertFrame (bt [0], 0, "X.Main()", line_main);

			AssertExecute ("call Hello (7)");
			AssertStopped (thread, "X.Hello(System.Int32)", line_hello_2);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello(System.Int32)", line_hello_3);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello()", line_hello);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 4)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 4);

			AssertFrame (bt [0], 0, "X.Hello()", line_hello);
			AssertFrame (bt [1], 1, "X.Hello(System.Int32)", line_hello_4);
			AssertInternalFrame (bt [2], 2);
			AssertFrame (bt [3], 3, "X.Main()", line_main);

			AssertExecute ("return -yes -invocation");
			AssertTargetOutput ("Done: 7 14 2");
			AssertNoTargetOutput ();

			AssertStopped (thread, "X.Main()", line_main);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 1)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 1);

			AssertFrame (bt [0], 0, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Done: 5 10 3");
			AssertTargetOutput ("3");
			AssertProcessExited (thread.Process);
			AssertTargetExited ();
		}
	}
}
