using System;
using System.Collections;
using System.IO;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerDispatchSink : IServerChannelSink, IChannelSinkBase
	{
		public IServerChannelSink NextChannelSink {
			get {
				return null;
			}
		}

		public IDictionary Properties {
			get {
				return null;
			}
		}

		public void AsyncProcessResponse (IServerResponseChannelSinkStack sinkStack, object state,
						  IMessage msg, ITransportHeaders headers, Stream stream)
		{
			Console.WriteLine ("SERVER ASYNC PROCESS RESPONSE: {0} {1} {2}", sinkStack, state, msg);

			ITransportHeaders responseHeaders = new TransportHeaders();

			if (sinkStack != null) stream = sinkStack.GetResponseStream (msg, responseHeaders);
			if (stream == null) stream = new MemoryStream();

			sinkStack.AsyncProcessResponse (msg, responseHeaders, stream);
		}

		public Stream GetResponseStream (IServerResponseChannelSinkStack sinkStack, object state,
						 IMessage msg, ITransportHeaders headers)
		{
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
			responseHeaders = null;
			responseStream = null;

			sinkStack.Push (this, null);

			DebuggerServerResponseSink responseSink = new DebuggerServerResponseSink (sinkStack);
			IMessageCtrl ctrl = ChannelServices.AsyncDispatchMessage (requestMsg, responseSink);

			responseMsg = null;
			return ServerProcessing.Async;
		}
	}
}
