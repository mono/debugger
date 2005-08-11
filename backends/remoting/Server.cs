using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;

using Mono.Debugger;
using Mono.Debugger.Remoting;

class Server
{
	static void Main ()
	{
		string url = "mdb://" + Environment.MachineName + ":" + Assembly.GetExecutingAssembly ().Location;

		DebuggerChannel channel = new DebuggerChannel (url);
		ChannelServices.RegisterChannel (channel);

		RemotingConfiguration.RegisterWellKnownServiceType (
			typeof (DebuggerBackend), "DebuggerBackend", WellKnownObjectMode.Singleton);

		channel.Connection.Run ();
		ChannelServices.UnregisterChannel (channel);
	}
}
