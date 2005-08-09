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
		static Thread server_thread;
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
			// IServerChannelSink next_sink = ChannelServices.CreateServerChannelSinkChain (provider, this);

                        sink = new DebuggerServerTransportSink (next_sink);
			channel_data = new DebuggerServerChannelData (url);

			DebuggerServerConnection.HandleConnection += new DebuggerServerConnection.ConnectionHandler (
				HandleConnection);
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

		public string[] GetUrlsForUri (string uri)
		{
			Console.WriteLine ("GET URLS FOR URI: {0}", uri);
			throw new NotSupportedException ();
			// return channel_data.ChannelUris;
		}

		public string Parse (string url, out string objectURI)
		{
			Console.Error.WriteLine ("SERVER PARSE: {0}", url);
			string host;
			string path = DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI);
			return "mdb://" + host + ":" + path;
		}

		bool aborted = false;

		void HandleConnection (Stream stream)
		{
			MessageStatus type = DebuggerMessageFormat.ReceiveMessageStatus (stream);

			Console.Error.WriteLine ("SERVER MESSAGE: {0}", type);

			switch (type) {
			case MessageStatus.Message:
				sink.InternalProcessMessage (stream);
				break;

			default:
				break;
			}
			stream.Flush ();
		}

		void WaitForConnections ()
		{
			DebuggerServerConnection.Start ();
		}

		public void StartListening (object data)
		{
			Console.Error.WriteLine ("START LISTENING: {0}", data);

			if (server_thread == null) {
#if FIXME
				string[] uris = new string [1];
				uris [0] = channel_url;
				channel_data.ChannelUris = uris;
#endif

				server_thread = new Thread (WaitForConnections);
				server_thread.Start ();
			}
		}

		public void StopListening (object data)
		{
			Console.Error.WriteLine ("STOP LISTENING: {0}", data);

			if (server_thread != null) {
				aborted = true;
				server_thread.Abort ();
				server_thread = null;
			}

			Console.Error.WriteLine ("STOPPED LISTENING");
		}

		public static void Run ()
		{
			server_thread.Join ();
		}
	}
}
