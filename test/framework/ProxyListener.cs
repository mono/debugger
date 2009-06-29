using System;
using NUnit.Core;
using NUnit.Core.Builders;
using NUnit.Core.Extensibility;

namespace Mono.Debugger.Test.Framework
{
	public class ProxyListener : MarshalByRefObject, EventListener
	{
		EventListener listener;

		public ProxyListener (EventListener listener)
		{
			this.listener = listener;
		}

		public void RunStarted (string name, int test_count)
		{
			listener.RunStarted (name, test_count);
		}

		public void RunFinished (TestResult result)
		{
			listener.RunFinished (result);
		}

		public void RunFinished (Exception exception)
		{
			listener.RunFinished (exception);
		}

		public void TestStarted (TestName test_name)
		{
			listener.TestStarted (test_name);
		}

		public void TestFinished (TestCaseResult result)
		{
			listener.TestFinished (result);
		}

		public void SuiteStarted (TestName test_name)
		{
			listener.SuiteStarted (test_name);
		}

		public void SuiteFinished (TestSuiteResult result)
		{
			listener.SuiteFinished (result);
		}

		public void UnhandledException (Exception exception)
		{
			listener.UnhandledException (exception);
		}

		public void TestOutput (TestOutput output)
		{
			listener.TestOutput (output);
		}
	}
}
