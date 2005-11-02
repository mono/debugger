using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Lifetime;

using Mono.Debugger;
using Mono.Debugger.Remoting;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServer : Debugger
	{
		static DebuggerChannel channel;
		DebuggerClient client;

		public static void Run (string url)
		{
			RemotingConfiguration.RegisterActivatedServiceType (
				typeof (DebuggerServer));

			// FIXME FIXME FIXME
			LifetimeServices.LeaseTime = TimeSpan.FromHours (3);
			LifetimeServices.LeaseManagerPollTime = TimeSpan.FromHours (3);
			LifetimeServices.RenewOnCallTime = TimeSpan.FromHours (3);

			channel = new DebuggerChannel (url);
			ChannelServices.RegisterChannel (channel);

			channel.Connection.Run ();
			ChannelServices.UnregisterChannel (channel);
		}

		public DebuggerServer (DebuggerManager manager, DebuggerClient client)
			: base (manager)
		{
			this.client = client;
		}

		protected override void DebuggerExited ()
		{
			client.Shutdown ();
			RemotingServices.Disconnect (this);
		}

		public Debugger Debugger {
			get { return this; }
		}
	}
}

