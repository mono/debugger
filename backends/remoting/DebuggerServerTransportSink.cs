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

		public void AsyncProcessResponse (IServerResponseChannelSinkStack sinkStack, object state,
						  IMessage msg, ITransportHeaders headers, Stream stream)
		{
			throw new NotImplementedException ();
		}

		public Stream GetResponseStream (IServerResponseChannelSinkStack sinkStack, object state,
						 IMessage msg, ITransportHeaders headers)
		{
			throw new NotImplementedException ();
		}

		public ServerProcessing ProcessMessage (IServerChannelSinkStack sinkStack,
							IMessage requestMsg,
							ITransportHeaders requestHeaders,
							Stream requestStream,
							out IMessage responseMsg,
							out ITransportHeaders responseHeaders,
							out Stream responseStream)
		{
			throw new NotSupportedException ();
		}

		internal void InternalProcessMessage (Stream in_stream, Stream out_stream)
		{
			string uri;
			DebuggerMessageFormat.MessageType msg_type;
			MemoryStream msg_stream;

			msg_stream = DebuggerMessageFormat.ReceiveMessageStream (
				in_stream, out msg_type, out uri);
			if (msg_type != DebuggerMessageFormat.MessageType.Request)
				throw new Exception ("received wrong message type");

			Console.Error.WriteLine ("SERVER MESSAGE: {0} {1} {2} {3}",
						 msg_stream, msg_stream.Length, msg_type, uri);


			ServerChannelSinkStack sink_stack = new ServerChannelSinkStack();
			sink_stack.Push (this, null);

			TransportHeaders headers = new TransportHeaders ();
			headers [CommonTransportKeys.RequestUri] = uri;

			IMessage resp_message;
			ITransportHeaders resp_headers;
			Stream resp_stream;
			ServerProcessing res = next_sink.ProcessMessage (sink_stack, null, headers, msg_stream,
									 out resp_message, out resp_headers,
									 out resp_stream);

			switch (res) {
			case ServerProcessing.Complete:
				Exception e = ((IMethodReturnMessage)resp_message).Exception;
				if (e != null) {
					// we handle exceptions in the transport channel
					DebuggerMessageFormat.SendExceptionMessage (out_stream, e.ToString ());
				} else {
					// send the response
					DebuggerMessageFormat.SendMessageStream (
						out_stream, (MemoryStream)resp_stream, 
						DebuggerMessageFormat.MessageType.Response, null);
				}
				break;
			case ServerProcessing.Async:
			case ServerProcessing.OneWay:
				throw new NotImplementedException ();
			}
		}
	}
}

