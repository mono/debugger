using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;

using Mono.Debugger.Remoting;

class Server
{
	static void Main ()
	{
		string url = "mdb://" + Environment.MachineName + ":" + Assembly.GetExecutingAssembly ().Location;
		DebuggerServerChannel channel = new DebuggerServerChannel (url);
		ChannelServices.RegisterChannel (channel);

		RemotingConfiguration.RegisterWellKnownServiceType (
			typeof (Foo), "Foo", WellKnownObjectMode.Singleton);

		DebuggerServerChannel.Run ();
		ChannelServices.UnregisterChannel (channel);
		Console.WriteLine ("SERVER EXIT");
	}
}
