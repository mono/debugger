using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientTransportSinkProvider : IClientChannelSinkProvider
	{
		public IClientChannelSinkProvider Next {
			get {
				return null;
			}

			set {
				throw new NotSupportedException ();
			}
		}

		public IClientChannelSink CreateSink (IChannelSender channel, string url,
						      object remoteChannelData)
		{
			return new DebuggerClientTransportSink ((DebuggerClientChannel) channel, url);
		}
	}
}
