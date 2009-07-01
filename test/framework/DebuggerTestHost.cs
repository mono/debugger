using System;
using System.IO;
using System.Collections;
using ST = System.Threading;
using SD = System.Diagnostics;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters.Binary;
using NUnit.Core;
using NUnit.Core.Builders;
using NUnit.Core.Extensibility;

namespace Mono.Debugger.Test.Framework
{
	public class DebuggerTestHost : MarshalByRefObject, IDebuggerTestHost, IDisposable
	{
		public delegate TestResult TestRunnerDelegate (string test_name, EventListener listener, ITestFilter filter);

		IDebuggerTestServer server;
		ST.ManualResetEvent startup_event = new ST.ManualResetEvent (false);
		SD.Process process;

		public static void InitializeRemoting ()
		{
                        var client_provider = new BinaryClientFormatterSinkProvider ();
                        var server_provider = new BinaryServerFormatterSinkProvider ();
                        server_provider.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;
                        
			Hashtable props = new Hashtable();
			props ["port"] = 0;
			TcpChannel tcp = new TcpChannel (props, client_provider, server_provider);

			ChannelServices.RegisterChannel (tcp, false);
		}

		protected DebuggerTestHost ()
		{
			string objref;
			using (MemoryStream ms = new MemoryStream ()) {
				BinaryFormatter bf = new BinaryFormatter ();
				ObjRef oref = RemotingServices.Marshal (this);
				bf.Serialize (ms, oref);
				objref = Convert.ToBase64String (ms.ToArray ());
			}

			process = SD.Process.Start (
				BuildInfo.mono, "--debug " + BuildInfo.builddir + "/build/debugger-test-server.exe " + objref);
			process.Exited += delegate {
				startup_event.Set ();
			};
			process.EnableRaisingEvents = true;

			startup_event.WaitOne (2500);

			if (server == null) {
				process.Kill ();
				process = null;
			}
		}

		public static DebuggerTestHost Create ()
		{
			try {
				DebuggerTestHost host = new DebuggerTestHost ();
				if (host.server == null)
					return null;

				return host;
			} catch {
				return null;
			}
		}

		public bool Run (TestInfo test, TestSuiteResult suite_result, string test_name, EventListener listener, ITestFilter filter, int timeout)
		{
			listener = new ProxyListener (listener);

			TestRunnerDelegate runner = new TestRunnerDelegate (delegate {
				return server.Run (test_name, listener, filter);
			});

			IAsyncResult ar = runner.BeginInvoke (test_name, null, filter, null, null);

			if (!ar.AsyncWaitHandle.WaitOne (timeout) || !ar.IsCompleted) {
				TestCaseResult error = new TestCaseResult (test);
				string msg = String.Format ("Timeout after {0} ms", timeout);
				error.Failure (msg, null, FailureSite.Parent);
				suite_result.AddResult (error);
				return false;
			}

			try {
				TestResult result = runner.EndInvoke (ar);
				if (result != null) {
					suite_result.AddResult (result);
					return true;
				}

				TestCaseResult error = new TestCaseResult (test);
				error.Failure ("Unknown error", null, FailureSite.Parent);
				suite_result.AddResult (error);
				return false;
			} catch (Exception ex) {
				TestCaseResult error = new TestCaseResult (test);
				string msg = String.Format ("Unknown exception: {0}", ex);
				error.Failure (msg, null, FailureSite.Parent);
				suite_result.AddResult (error);
				return false;
			}
		}

		public void Shutdown ()
		{
			try {
				if (server != null)
					server.Shutdown ();
			} catch {
				;
			} finally {
				server = null;
			}

			try {
				if (process != null) {
					process.Kill ();
					process.WaitForExit ();
				}
			} catch {
				;
			} finally {
				process = null;
				RemotingServices.Disconnect (this);
			}
		}

		public void RegisterServer (IDebuggerTestServer server)
		{
			this.server = server;
			startup_event.Set ();
		}

		protected virtual void Dispose (bool disposing)
		{
			if (disposing) {
				Shutdown ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DebuggerTestHost ()
		{
			Dispose (false);
		}
	}
}
