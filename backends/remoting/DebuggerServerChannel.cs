using System;
using System.IO;
using System.Threading;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerChannel : IChannelReceiver, IChannel
	{
		DebuggerServerTransportSink sink;
		ChannelDataStore channel_data;
		Thread server_thread;
		int priority = 1;

		public DebuggerServerChannel ()
		{
			IServerChannelSinkProvider provider = new BinaryServerFormatterSinkProvider ();
			IServerChannelSink next_sink = ChannelServices.CreateServerChannelSinkChain (provider, this);

                        sink = new DebuggerServerTransportSink (next_sink);	
			channel_data = new ChannelDataStore (null);
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
			Console.Error.WriteLine ("GET URLS FOR URI: {0}", uri);
			return new string [0];
		}

		public string Parse (string url, out string objectURI)
		{
			Console.Error.WriteLine ("SERVER PARSE: {0}", url);
			string host;
			return DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI);
		}

		bool aborted = false;

		void WaitForConnections ()
		{
			Stream in_stream = Console.OpenStandardInput ();
			Stream out_stream = Console.OpenStandardOutput ();

			while (!aborted) {
				MessageStatus type = DebuggerMessageFormat.ReceiveMessageStatus (in_stream);

				Console.Error.WriteLine ("SERVER MESSAGE: {0}", type);

				switch (type) {
				case MessageStatus.MethodMessage:
					sink.InternalProcessMessage (in_stream, out_stream);
					break;

				case MessageStatus.Unknown:
				case MessageStatus.CancelSignal:
					aborted = true;
					break;
				}
				in_stream.Flush ();
				out_stream.Flush ();
			}
		}

		public void StartListening (object data)
		{
			Console.Error.WriteLine ("START LISTENING: {0}", data);

			if (server_thread == null) {
				string[] uris = new string [1];
				uris [0] = "mdb://gondor:/home/martin/monocvs/debugger/backends/remoting/Sleep.exe!Foo";
				channel_data.ChannelUris = uris;

				server_thread = new Thread (new ThreadStart (WaitForConnections));
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
			Environment.Exit (0);
		}
	}
}
