using System;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerChannel : IChannel, IChannelSender, IChannelReceiver
	{
		DebuggerServerChannel server_channel = new DebuggerServerChannel ();
		DebuggerClientChannel client_channel = new DebuggerClientChannel ();
		
		public object ChannelData {
			get { return server_channel.ChannelData; }
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return server_channel.ChannelPriority; }
		}

		public IMessageSink CreateMessageSink (string url,
						       object remoteChannelData,
						       out string objectURI)
		{
			return client_channel.CreateMessageSink (url, remoteChannelData, out objectURI);
		}

		public string Parse (string url, out string objectURI)
		{
			string host;
			return ParseDebuggerURL (url, out host, out objectURI);
		}

		internal static string ParseDebuggerURL (string url, out string host, out string objectURI)
		{
			objectURI = null;
			host = null;

			if (!url.StartsWith ("mdb://"))
				return null;

			int pos = url.IndexOf ('!', 6);
			if (pos == -1) return null;
			string path = url.Substring (6, pos - 6);

			objectURI = url.Substring (pos + 1);

			int colon = path.IndexOf (':');
			if (colon > 0) {
				host = path.Substring (0, colon);
				path = path.Substring (colon + 1);
			}

			Console.WriteLine ("PARSE: |{0}|{1}|{2}|", objectURI, path, host);

			return path;
		}

		public string[] GetUrlsForUri (string uri)
		{
			return server_channel.GetUrlsForUri (uri);
		}

		public void StartListening (object data)
		{
			server_channel.StartListening (data);
		}

		public void StopListening (object data)
		{
			server_channel.StopListening (data);
		}
	}
}
