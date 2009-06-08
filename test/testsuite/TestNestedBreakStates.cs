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
			int unhandled_catch = AssertUnhandledCatchpoint ("Exception");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "test", "X.Test()");

			AssertExecuteInBackground ("print -nested-break UnhandledException ()");
			AssertNestedBreakState (thread, "X.UnhandledException()", GetLine ("unhandled"));

			AssertPrint (thread, "catch",
				     "(MyUnhandledException) { \"MyUnhandledException: Exception of type 'MyUnhandledException' " +
				     "was thrown.\n  at X.UnhandledException () [0x00000] in " + FileName + ":" +
				     GetLine ("unhandled") + " \" }");

			AssertExecute ("continue");
			e = AssertEvent (DebuggerEventType.CommandDone);
			if (!(e.Data is ScriptingException))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));
			string message = ((ScriptingException) e.Data).Message;
			if (!message.StartsWith ("Invocation of `UnhandledException ()' raised an exception: MyUnhandledException"))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));

			AssertExecute ("continue");

			AssertCaughtException (thread, "X.Exception()", GetLine ("first"));

			AssertExecuteInBackground ("print -nested-break OtherException ()");
			AssertNestedBreakState (thread, "X.OtherException()", GetLine ("second"));

			AssertExecute ("continue");
			e = AssertEvent (DebuggerEventType.CommandDone);
			if (!(e.Data is ScriptingException))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));
			message = ((ScriptingException) e.Data).Message;
			if (!message.StartsWith ("Invocation of `OtherException ()' raised an exception: MyException: second"))
				Assert.Fail (String.Format ("Got unknown event: {0}", e));

			AssertExecute ("continue");

			AssertCaughtException (thread, "X.HandledException()", GetLine ("rethrow"));

			AssertExecute ("continue");
			AssertTargetExited (process);
		}
	}
}
