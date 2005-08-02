using System;
using System.Threading;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerTest : MarshalByRefObject
	{
		public static void Main ()
		{
			DebuggerChannel channel = new DebuggerChannel ();
			ChannelServices.RegisterChannel (channel);

			RemotingConfiguration.RegisterWellKnownServiceType (
				typeof (Foo), "Foo", WellKnownObjectMode.Singleton);
			Console.WriteLine ();

			Thread.Sleep (100000);
		}
	}
}
