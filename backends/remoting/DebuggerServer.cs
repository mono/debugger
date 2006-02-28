using System;
using System.IO;
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
		DebuggerSession session;

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

		public DebuggerServer (DebuggerClient client, ReportWriter writer)
			: base (client)
		{
			this.client = client;
			Report.Initialize (writer);
			this.session = new DebuggerSession (client);
		}

		public DebuggerSession Session {
			get { return session; }
		}

		public DebuggerSession LoadSession (Stream stream)
		{
			try {
				session = DebuggerSession.Load (client, stream);
			} catch (Exception ex) {
				Console.WriteLine ("EX: {0}", ex);
			}
			return session;
		}

		protected override void DebuggerExited ()
		{
			client.Shutdown ();
			RemotingServices.Disconnect (this);
		}
	}
}
