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
			Console.WriteLine ("SYNC PROCESS MESSAGE: {0} {1} {2}", stack, msg, msg.GetType ());

			ITransportHeaders headers = new TransportHeaders();
			Stream stream = stack.GetResponseStream (msg, headers);

			stack.AsyncProcessResponse (msg, headers, stream);

			Console.WriteLine ("SYNC PROCESS MESSAGE #1: {0}", msg);
			return null;
		}

		IMessageCtrl IMessageSink.AsyncProcessMessage (IMessage msg, IMessageSink replySink)
		{
			Console.WriteLine ("ASYNC PROCESS MESSAGE: {0} {1}", msg, replySink);
			throw new NotSupportedException ();
		}

	}
}
