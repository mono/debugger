using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestAbort : DebuggerTestFixture
	{
		public TestAbort ()
			: base ("TestAbort")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 33;
			const int line_hello = 13;
			const int line_hello_2 = 20;
			const int line_hello_3 = 22;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("call Hello()");
			AssertStopped (thread, "X.Hello()", line_hello);

			Backtrace bt = thread.GetBacktrace (-1);
			if (bt.Count != 3)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 5);

			AssertFrame (bt [0], 0, "X.Hello()", line_hello);
			AssertRuntimeInvokeFrame (bt [1], 1, "X.Hello()");
			AssertFrame (bt [2], 2, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertRuntimeInvokeDone (thread, "X.Main()", line_main);

			AssertExecute ("call Hello()");
			AssertStopped (thread, "X.Hello()", line_hello);

			AssertExecute ("return -yes");

			AssertRuntimeInvokeDone (thread, "X.Main()", line_main);

			AssertExecute ("call Hello (8)");
			AssertStopped (thread, "X.Hello(int)", line_hello_2);
			AssertExecute ("return -yes");
			AssertRuntimeInvokeDone (thread, "X.Main()", line_main);

			AssertExecute ("call Hello (9)");
			AssertStopped (thread, "X.Hello(int)", line_hello_2);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello(int)", line_hello_3);
			AssertExecute ("return -yes");
			AssertTargetOutput ("Done: 9 18 1");
			AssertNoTargetOutput ();
			AssertRuntimeInvokeDone (thread, "X.Main()", line_main);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 1)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 1);

			AssertFrame (bt [0], 0, "X.Main()", line_main);

			AssertExecute ("call Hello (7)");
			AssertStopped (thread, "X.Hello(int)", line_hello_2);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello(int)", line_hello_3);
			AssertExecute ("step");
			AssertStopped (thread, "X.Hello()", line_hello);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 4)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 6);

			AssertFrame (bt [0], 0, "X.Hello()", line_hello);
			AssertFrame (bt [1], 1, "X.Hello(int)", line_hello_3);
			AssertRuntimeInvokeFrame (bt [2], 2, "X.Hello(int)");
			AssertFrame (bt [3], 3, "X.Main()", line_main);

			AssertExecute ("return -yes -invocation");
			AssertTargetOutput ("Done: 7 14 2");
			AssertNoTargetOutput ();

			AssertRuntimeInvokeDone (thread, "X.Main()", line_main);

			bt = thread.GetBacktrace (-1);
			if (bt.Count != 1)
				Assert.Fail ("Backtrace has {0} frames, but expected {1}.",
					     bt.Count, 1);

			AssertFrame (bt [0], 0, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Done: 5 10 3");
			AssertTargetOutput ("3");
			AssertTargetExited (thread.Process);
		}
	}
}
