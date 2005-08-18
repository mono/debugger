using System;
using System.Diagnostics;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientChannel : IChannelSender, IChannel, IDisposable
	{
		DebuggerServerChannel server_channel;
		IClientChannelSinkProvider sink_provider;
		DebuggerConnection server_connection;
		int priority = 1;

		public DebuggerClientChannel (DebuggerServerChannel server_channel)
		{
                        sink_provider = new BinaryClientFormatterSinkProvider ();
                        sink_provider.Next = new DebuggerClientTransportSinkProvider ();
			this.server_channel = server_channel;
		}

		public DebuggerClientChannel (DebuggerServerChannel server_channel,
					      DebuggerConnection server_connection)
			: this (server_channel)
		{
			this.server_connection = server_connection;
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return priority; }
		}

		public IMessageSink CreateMessageSink (string url, object remote_data,
						       out string object_uri)
	        {
			if (DebuggerChannel.ParseDebuggerURL (url, out object_uri) != null)
				return (IMessageSink) sink_provider.CreateSink (this, url, remote_data);

			DebuggerServerChannelData data = remote_data as DebuggerServerChannelData;
			if (data != null) {
				string path = data.ChannelURL + "/" + url;
				return (IMessageSink) sink_provider.CreateSink (this, path, data);
			}

			return null;
		}

		public string Parse (string url, out string object_uri)
		{
			return DebuggerChannel.ParseDebuggerURL (url, out object_uri);
		}

		internal DebuggerConnection GetConnection (string channel_uri)
		{
			if (server_connection != null)
				return server_connection;

			return DebuggerClient.GetConnection (channel_uri);
		}

#region IDisposable implementation
		~DebuggerClientChannel ()
		{
			Dispose (false);
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
#if FIXME
				foreach (DebuggerConnection connection in connections.Values)
					((IDisposable) connection).Dispose ();
#endif
			}

			disposed = true;
		}


		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion
	}
}
