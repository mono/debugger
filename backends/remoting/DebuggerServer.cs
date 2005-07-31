using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;

namespace Mono.Debugger
{
	public class DebuggerServer
	{
		public DebuggerServer ()
		{
		}

		public static int Main (string [] args)
		{
			TcpChannel chan = new TcpChannel (8086);
			ChannelServices.RegisterChannel (chan);

			RemotingConfiguration.RegisterWellKnownServiceType (
				typeof (DebuggerBackend), "DebuggerServer",
				WellKnownObjectMode.Singleton);

			Console.WriteLine ("Server Activated");
			Report.CurrentDebugFlags = 4095;
			Console.ReadLine();

			return 0;
		}
	}
}
