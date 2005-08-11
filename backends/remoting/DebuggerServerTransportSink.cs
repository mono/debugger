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
			public readonly DebuggerConnection Connection;
			public readonly long SequenceID;

			public MessageData (DebuggerConnection connection, long id)
			{
				this.Connection = connection;
				this.SequenceID = id;
			}
		}

		public void AsyncProcessResponse (IServerResponseChannelSinkStack sink_stack, object state,
						  IMessage msg, ITransportHeaders headers, Stream stream)
		{
			MessageData data = (MessageData) state;
			data.Connection.SendAsyncResponse (data.SequenceID, stream, headers);
		}

		public Stream GetResponseStream (IServerResponseChannelSinkStack sink_stack, object state,
						 IMessage msg, ITransportHeaders headers)
		{
			return null;
		}

		public ServerProcessing ProcessMessage (IServerChannelSinkStack sink_stack,
							IMessage request_message,
							ITransportHeaders request_headers,
							Stream request_stream,
							out IMessage response_message,
							out ITransportHeaders response_headers,
							out Stream response_stream)
		{
			return next_sink.ProcessMessage (
				sink_stack, request_message, request_headers, request_stream,
				out response_message, out response_headers, out response_stream);
		}

		internal ServerProcessing InternalProcessMessage (DebuggerConnection connection, long sequence_id,
								  Stream request_stream,
								  ITransportHeaders request_headers,
								  out Stream response_stream,
								  out ITransportHeaders response_headers)
		{
			ServerChannelSinkStack sink_stack = new ServerChannelSinkStack ();
			sink_stack.Push (this, new MessageData (connection, sequence_id));

			IMessage response_message;

			return ProcessMessage (
				sink_stack, null, request_headers, request_stream,
				out response_message, out response_headers, out response_stream);
		}
	}
}

