using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestNestedBreakStates : TestSuite
	{
		public TestNestedBreakStates ()
			: base ("TestNestedBreakStates")
		{
			Config.NestedBreakStates = true;
		}

		public override void SetUp ()
		{
			base.SetUp ();
			Interpreter.Options.StopInMain = false;
		}

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertHitBreakpoint (thread, "main", "X.Main()");

			AssertExecuteInBackground ("print -nested-break x.Test ()");
			AssertNestedBreakState (thread, "X.Test()", GetLine ("test"));

			AssertExecute ("continue");

			DebuggerEvent e = AssertEvent (DebuggerEventType.CommandDone);
			Assert.AreEqual (e.Data, "(double) 3.14159265358979");

			int catchpoint = AssertCatchpoint ("MyException");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test", "X.Test()");
			AssertExecute ("continue");

			AssertCaughtException (thread, "X.Exception()", GetLine ("first"));

			AssertExecuteInBackground ("print -nested-break OtherException ()");
			AssertNestedBreakState (thread, "X.OtherException()", GetLine ("second"));

			AssertExecute ("continue");
			e = AssertEvent (DebuggerEventType.CommandDone);
			if (!(e.Data is ScriptingException))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));
			string message = ((ScriptingException) e.Data).Message;
			if (!message.StartsWith ("Invocation of `OtherException ()' raised an exception: MyException: second"))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));

			AssertExecute ("continue");

			AssertCaughtException (thread, "X.HandledException()", GetLine ("rethrow"));

			AssertExecute ("continue");
			AssertTargetExited (process);
		}
	}
}
