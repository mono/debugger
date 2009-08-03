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
	public class TestExpressionEvaluator : DebuggerTestFixture
	{
		public TestExpressionEvaluator ()
			: base ("TestExpressionEvaluator")
		{ }

		[Test]
		[Category("NotWorking")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");
			AssertExecute ("next");
			AssertStopped (thread, "main+1", "X.Main()");

			EE.IExpression expr = Interpreter.ExpressionParser.Parse ("a.Sleep ().Sleep ().Sleep ()");
			Console.WriteLine ("TEST: {0}", expr);

			EE.EvaluationFlags flags = EE.EvaluationFlags.NestedBreakStates;

			EE.AsyncResult async = expr.Evaluate (thread.CurrentFrame, flags,
				delegate (EE.EvaluationResult result, object data) {
					Console.WriteLine ("EVALUATION DONE: {0} {1}", result, data);
			});

			bool completed = async.AsyncWaitHandle.WaitOne (700);

			Console.WriteLine ("ASYNC WAIT DONE: {0}", completed);

			async.Abort ();
			async.AsyncWaitHandle.WaitOne ();

			Console.WriteLine ("EVALUATION DONE");

			AssertExecute ("continue");
			AssertTargetExited (thread.Process);
		}
	}
}
