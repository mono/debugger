using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization.Formatters.Binary;
using NUnit.Core;
using NUnit.Core.Builders;
using NUnit.Core.Extensibility;

namespace Mono.Debugger.Test.Framework
{
	public class DebuggerTestServer : MarshalByRefObject, IDebuggerTestServer
	{
		static ManualResetEvent shutdown_event = new ManualResetEvent (false);
		DebuggerTestFixtureBuilder builder = new DebuggerTestFixtureBuilder ();

		static void Main (string[] args)
		{
			DebuggerTestHost.InitializeRemoting ();

			IDebuggerTestHost host;
			using (MemoryStream ms = new MemoryStream (Convert.FromBase64String (args [0]))) {
				BinaryFormatter bf = new BinaryFormatter ();
				host = (IDebuggerTestHost) bf.Deserialize (ms);
			}

			DebuggerTestServer server = new DebuggerTestServer ();
			host.RegisterServer (server);

			shutdown_event.WaitOne ();

			RemotingServices.Disconnect (server);
		}

		public TestResult Run (string test_name, EventListener listener, ITestFilter filter)
		{
			try {
				Type test_type = Type.GetType (test_name, true);
				NUnit.Core.Test test = builder.BuildFrom (test_type);
				TestResult result = test.Run (listener, filter);
				return result;
			} catch (Exception ex) {
				Console.WriteLine ("RUN EX: {0}", ex);
				throw;
			}
		}

		[OneWay]
		public void Shutdown ()
		{
			shutdown_event.Set ();
		}
	}

	public class DebuggerTestFixtureBuilder : NUnitTestFixtureBuilder
	{
		public const string DebuggerTestFixtureAttribute = "Mono.Debugger.Tests.DebuggerTestFixtureAttribute";

		public override bool CanBuildFrom (Type type)
		{
			return Reflect.HasAttribute (type, DebuggerTestFixtureAttribute, true);
		}
	}
}
