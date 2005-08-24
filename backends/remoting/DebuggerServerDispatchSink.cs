using System;
using System.Collections;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

using Mono.Debugger.Backends;

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

		public void AsyncProcessResponse (IServerResponseChannelSinkStack sink_stack, object state,
						  IMessage msg, ITransportHeaders headers, Stream stream)
		{
			ITransportHeaders response_headers = new TransportHeaders();

			if (sink_stack != null) stream = sink_stack.GetResponseStream (msg, response_headers);
			if (stream == null) stream = new MemoryStream();

			sink_stack.AsyncProcessResponse (msg, response_headers, stream);
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

			ServerProcessing proc;

			IMethodCallMessage message = (IMethodCallMessage) requestMsg;
			bool is_command = message.MethodBase.IsDefined (
				typeof (SingleSteppingEngine.CommandAttribute), false);

			DebuggerServerResponseSink sink = new DebuggerServerResponseSink (sinkStack);

			if (is_command) {
				responseMsg = DebuggerServer.ThreadManager.SendCommand (message, sink);
				if (responseMsg != null)
					return ServerProcessing.Complete;
				else
					return ServerProcessing.Async;
			}

			if (RemotingServices.IsOneWay (message.MethodBase))
				proc = ServerProcessing.OneWay;
			else
				proc = ServerProcessing.Async;

			IMessageCtrl ctrl = ChannelServices.AsyncDispatchMessage (requestMsg, sink);

			responseMsg = null;
			return proc;
		}
	}
}
