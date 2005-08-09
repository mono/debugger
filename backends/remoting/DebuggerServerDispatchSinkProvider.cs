using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerDispatchSinkProvider : IServerChannelSinkProvider
	{
		public IServerChannelSinkProvider Next {
			get {
				return null;
			}

			set {
				throw new NotSupportedException ();
			}
		}

		public IServerChannelSink CreateSink (IChannelReceiver channel)
		{
			return new DebuggerServerDispatchSink ();
		}

		public void GetChannelData (IChannelDataStore channelData)
		{ }
	}
}
