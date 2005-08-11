using System;
using System.Collections;
using System.IO;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServerResponseSink : IMessageSink
	{
		IServerChannelSinkStack stack;

		public DebuggerServerResponseSink (IServerChannelSinkStack stack)
		{
			this.stack = stack;
		}

		public IMessageSink NextSink {
			get {
				return null;
			}
		}

		public IDictionary Properties {
			get {
				return null;
			}
		}

		IMessage IMessageSink.SyncProcessMessage (IMessage msg)
		{
			ITransportHeaders headers = new TransportHeaders();
			Stream stream = stack.GetResponseStream (msg, headers);

			stack.AsyncProcessResponse (msg, headers, stream);

			return null;
		}

		IMessageCtrl IMessageSink.AsyncProcessMessage (IMessage msg, IMessageSink replySink)
		{
			throw new NotSupportedException ();
		}

	}
}
