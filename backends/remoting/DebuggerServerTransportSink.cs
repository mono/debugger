using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerTransportSink : IServerChannelSink
	{
		IServerChannelSink next_sink;

		public DebuggerServerTransportSink (IServerChannelSink next_sink)
		{
			this.next_sink = next_sink;
		}

		public IServerChannelSink NextChannelSink {
			get {
				return next_sink;
			}
		}

		public IDictionary Properties {
			get {
				throw new NotImplementedException ();
			}
		}

		struct MessageData {
			public readonly Stream Stream;
			public readonly long SequenceID;

			public MessageData (Stream stream, long id)
			{
				this.Stream = stream;
				this.SequenceID = id;
			}
		}

		public void AsyncProcessResponse (IServerResponseChannelSinkStack sinkStack, object state,
						  IMessage msg, ITransportHeaders headers, Stream stream)
		{
			MessageData data = (MessageData) state;
			Console.WriteLine ("AYNC RESPONSE: {0}", data.SequenceID);
			DebuggerMessageFormat.SendMessageStream (data.Stream, stream, data.SequenceID, headers);
		}

		public Stream GetResponseStream (IServerResponseChannelSinkStack sinkStack, object state,
						 IMessage msg, ITransportHeaders headers)
		{
			Console.WriteLine ("TRANSPORT SINK GET RESPONSE STREAM");
			return null;
		}

		public ServerProcessing ProcessMessage (IServerChannelSinkStack sinkStack,
							IMessage requestMsg,
							ITransportHeaders requestHeaders,
							Stream requestStream,
							out IMessage responseMsg,
							out ITransportHeaders responseHeaders,
							out Stream responseStream)
		{
			Console.WriteLine ("TRANSPORT SINK PROCESS MESSAGE");
			throw new NotSupportedException ();
		}

		internal void InternalProcessMessage (Stream network_stream)
		{
			long sequence_id;
			ITransportHeaders requestHeaders;
			MemoryStream requestStream = DebuggerMessageFormat.ReceiveMessageStream (
				network_stream, out sequence_id, out requestHeaders);

			Console.Error.WriteLine ("SERVER PROCESS MESSAGE: {0}", next_sink.NextChannelSink);

			ServerChannelSinkStack sinkStack = new ServerChannelSinkStack ();
			sinkStack.Push (this, new MessageData (network_stream, sequence_id));

			IMessage responseMsg;
			ITransportHeaders responseHeaders;
			Stream responseStream;

			ServerProcessing proc = next_sink.ProcessMessage (
				sinkStack, null, requestHeaders, requestStream, out responseMsg,
				out responseHeaders, out responseStream);

			Console.Error.WriteLine ("SERVER PROCESSED MESSAGE: {0} {1}", sequence_id, proc);

			switch (proc) {
			case ServerProcessing.Complete:
				DebuggerMessageFormat.SendMessageStream (
					network_stream, responseStream, sequence_id, responseHeaders);
				break;
			case ServerProcessing.Async:
			case ServerProcessing.OneWay:
				break;
			}
		}
	}
}

