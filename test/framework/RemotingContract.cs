using System;
using System.Runtime.Remoting.Messaging;
using NUnit.Core;

namespace Mono.Debugger.Test.Framework
{
	public interface IDebuggerTestHost
	{
		void RegisterServer (IDebuggerTestServer server);
	}

	public interface IDebuggerTestServer
	{
		TestResult Run (string test_type, EventListener listener, ITestFilter filter);

		[OneWay]
		void Shutdown ();
	}
}
