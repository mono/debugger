using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;
using EE = Mono.Debugger.ExpressionEvaluator;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestToString2 : DebuggerTestFixture
	{
		public TestToString2 ()
			: base ("TestToString2")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, "main2", "X.Main()");

			TargetClassObject a = EvaluateExpression (thread, "a") as TargetClassObject;
			TargetClassObject b = EvaluateExpression (thread, "b") as TargetClassObject;

			Assert.IsTrue (a != null);
			Assert.IsTrue (b != null);

			string text;
			EE.EvaluationResult result;

			result = EE.MonoObjectToString (thread, a, EE.EvaluationFlags.None, 500, out text);

			if (result != ExpressionEvaluator.EvaluationResult.Ok)
				Assert.Fail ("Failed to print `a': got result {0}", result);
			if (text != "Foo (3)")
				Assert.Fail ("Failed to print `a': got result {0}", text);

			result = EE.MonoObjectToString (thread, b, EE.EvaluationFlags.None, 500, out text);
			if (result != EE.EvaluationResult.Timeout)
				Assert.Fail ("Failed to print `a': got result {0}", result);

			AssertExecute ("continue");
			AssertTargetOutput ("TEST: Foo (3) Foo (8)");
			AssertTargetExited (thread.Process);
		}
	}
}
