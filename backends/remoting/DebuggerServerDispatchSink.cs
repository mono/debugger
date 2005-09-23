using System;
using System.Collections;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;
using System.Runtime.Serialization;

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
			if (RemotingServices.IsOneWay (((IMethodMessage) msg).MethodBase))
				return;

			ITransportHeaders response_headers = new TransportHeaders();

			if (sink_stack != null) stream = sink_stack.GetResponseStream (msg, response_headers);
			if (stream == null) stream = new MemoryStream();

			try {
				sink_stack.AsyncProcessResponse (msg, response_headers, stream);
			} catch (SerializationException ex) {
				// FIXME: Bug #76001
				Console.WriteLine ("EXCEPTION: {0}", ex.Message);
			} catch (Exception ex) {
				Console.WriteLine (ex);
				throw;
			}
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

			ServerProcessing proc;

			sinkStack.Push (this, null);

			IMethodCallMessage message = (IMethodCallMessage) requestMsg;
			bool is_command = message.MethodBase.IsDefined (typeof (CommandAttribute), false);

			Report.Debug (DebugFlags.Remoting, "Dispatch {0} {1}",
				      message.MethodBase.DeclaringType, message.MethodBase);

			DebuggerServerResponseSink sink = new DebuggerServerResponseSink (sinkStack);

			if (is_command) {
				responseMsg = DebuggerContext.ThreadManager.SendCommand (message, sink);
				if (responseMsg != null) {
					Report.Debug (DebugFlags.Remoting, "Dispatch completed {0}",
						      responseMsg);
					return ServerProcessing.Complete;
				} else
					return ServerProcessing.Async;
			}

			if (RemotingServices.IsOneWay (message.MethodBase))
				proc = ServerProcessing.OneWay;
			else
				proc = ServerProcessing.Async;

			ChannelServices.AsyncDispatchMessage (requestMsg, sink);

			responseMsg = null;
			return proc;
		}
	}
}
