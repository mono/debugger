using System;
using System.Diagnostics;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientChannel : IChannelSender, IChannel
	{
		IClientChannelSinkProvider sink_provider;
		int priority = 1;

		public DebuggerClientChannel ()
		{
                        sink_provider = new BinaryClientFormatterSinkProvider ();
                        sink_provider.Next = new DebuggerClientTransportSinkProvider ();
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return priority; }
		}

		public IMessageSink CreateMessageSink (string url,
						       object remoteChannelData,
						       out string objectURI)
	        {
			Console.WriteLine ("CREATE MESSAGE SINK: {0} {1}", url, remoteChannelData);

			string host;
			if (DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI) == null)
				return null;

			return (IMessageSink) sink_provider.CreateSink (this, url, remoteChannelData);
		}

		public string Parse (string url, out string objectURI)
		{
			Console.WriteLine ("CLIENT PARSE: {0}", url);
			string host;
			return DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI);
		}
	}
}
