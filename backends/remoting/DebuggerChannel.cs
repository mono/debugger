using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerChannel : IChannelReceiver, IChannelSender, IChannel
	{
		DebuggerConnection connection;
		DebuggerServerChannel server_channel;
		DebuggerClientChannel client_channel;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_remoting_setup_server ();

		public DebuggerChannel (string url)
		{
			int fd = mono_debugger_remoting_setup_server ();
			server_channel = new DebuggerServerChannel (url);
			connection = new DebuggerConnection (server_channel, url, fd);
			client_channel = new DebuggerClientChannel (server_channel, connection);
		}

		public DebuggerChannel ()
		{
			server_channel = new DebuggerServerChannel (null);
			client_channel = new DebuggerClientChannel (server_channel);
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return 1; }
		}

		public DebuggerConnection Connection {
			get { return connection; }
		}

		public IMessageSink CreateMessageSink (string url, object remoteChannelData, out string objectURI)
		{
			return client_channel.CreateMessageSink (url, remoteChannelData, out objectURI);
		}

		public void StartListening (object data)
		{
			server_channel.StartListening (data);
		}

		public void StopListening (object data)
		{
			server_channel.StopListening (data);
		}

		public string[] GetUrlsForUri (string uri)
		{
			return server_channel.GetUrlsForUri (uri);
		}

		public object ChannelData {
			get {
				return server_channel.ChannelData;
			}
		}

		public string Parse (string url, out string object_uri)
		{
			return DebuggerChannel.ParseDebuggerURL (url, out object_uri);
		}

		internal DebuggerServerChannel ServerChannel {
			get { return server_channel; }
		}

		internal static string ParseDebuggerURL (string url, out string objectURI)
		{
			objectURI = null;

			if (!url.StartsWith ("mdb://"))
				return null;

			int pos = url.IndexOf ('!', 6);
			if (pos == -1) return null;

			string path = url.Substring (6, pos - 6);
			objectURI = url.Substring (pos + 1);

			return "mdb://" + path;
		}
	}
}
