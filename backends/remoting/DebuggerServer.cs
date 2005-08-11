using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;

using Mono.Debugger;
using Mono.Debugger.Remoting;

class Server
{
	static void Main (string[] args)
	{
		string host = args [0];
		string path = args [1];

		string url;
		if (host != "")
			url = "mdb://" + host + ":" + path;
		else
			url = "mdb://" + path;

		RemotingConfiguration.RegisterWellKnownServiceType (
			typeof (DebuggerBackend), "DebuggerBackend", WellKnownObjectMode.Singleton);

		DebuggerChannel channel = new DebuggerChannel (url);
		ChannelServices.RegisterChannel (channel);

		channel.Connection.Run ();
		ChannelServices.UnregisterChannel (channel);
	}
}
