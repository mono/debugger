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
			ITransportHeaders requestHeaders;
			Stream requestStream = DebuggerMessageFormat.ReceiveMessageStream (
				in_stream, out requestHeaders);

			Console.Error.WriteLine ("SERVER PROCESS MESSAGE");

			ServerChannelSinkStack sinkStack = new ServerChannelSinkStack ();
			sinkStack.Push (this, null);

			IMessage responseMsg;
			ITransportHeaders responseHeaders;
			Stream responseStream;

			ServerProcessing proc = next_sink.ProcessMessage (
				sinkStack, null, requestHeaders, requestStream, out responseMsg,
				out responseHeaders, out responseStream);

			Console.Error.WriteLine ("SERVER PROCESSED MESSAGE: {0}", proc);

			switch (proc) {
			case ServerProcessing.Complete:
				DebuggerMessageFormat.SendMessageStream (
					out_stream, responseStream, responseHeaders);
				out_stream.Flush ();
				break;
			case ServerProcessing.Async:
			case ServerProcessing.OneWay:
				throw new NotImplementedException ();
			}
		}
	}
}

