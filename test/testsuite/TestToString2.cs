using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestToString2 : TestSuite
	{
		public TestToString2 ()
			: base ("TestToString2")
		{ }

		[Test]
		[Category("Test")]
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
			ExpressionParser.EvaluationResult result;

			result = ExpressionParser.MonoObjectToString (thread, a, 1000, out text);

			if (result != ExpressionParser.EvaluationResult.Ok)
				Assert.Fail ("Failed to print `a': got result {0}", result);
			if (text != "(Foo) { \"Foo (5)\" }")
				Assert.Fail ("Failed to print `a': got result {0}", text);


			result = ExpressionParser.MonoObjectToString (thread, b, 1000, out text);
			if (result != ExpressionParser.EvaluationResult.Timeout)
				Assert.Fail ("Failed to print `a': got result {0}", result);

			AssertExecute ("continue");
			AssertTargetOutput ("TEST: Foo (5) Foo (13)");
			AssertTargetExited (thread.Process);
		}
	}
}
