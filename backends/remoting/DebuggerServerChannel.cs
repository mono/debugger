using System;
using System.IO;
using System.Threading;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	[Serializable]
	public class DebuggerServerChannelData
	{
		public readonly string ChannelURL;

		public DebuggerServerChannelData (string channel_url)
		{
			this.ChannelURL = channel_url;
		}
	}

	public class DebuggerServerChannel : IChannelReceiver, IChannel
	{
		DebuggerServerTransportSink sink;
		object channel_data;
		int priority = 1;

		string channel_url;

		public DebuggerServerChannel (string url)
		{
			this.channel_url = url;

			IServerChannelSinkProvider provider = new BinaryServerFormatterSinkProvider ();

			IServerChannelSinkProvider temp = provider;
			while (temp.Next != null)
				temp = temp.Next;
			temp.Next = new DebuggerServerDispatchSinkProvider ();

			IServerChannelSink next_sink = provider.CreateSink (this);

                        sink = new DebuggerServerTransportSink (next_sink);
			channel_data = new DebuggerServerChannelData (url);
		}

		public object ChannelData {
			get { return channel_data; }
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return priority; }
		}

		public DebuggerServerTransportSink Sink {
			get { return sink; }
		}

		public string[] GetUrlsForUri (string uri)
		{
			Console.WriteLine ("GET URLS FOR URI: {0}", uri);
			throw new NotSupportedException ();
			// return channel_data.ChannelUris;
		}

		public string Parse (string url, out string objectURI)
		{
			string host, path;
			return DebuggerChannel.ParseDebuggerURL (url, out host, out path, out objectURI);
		}

		bool aborted = false;

		public void StartListening (object data)
		{
		}

		public void StopListening (object data)
		{
		}
	}
}
