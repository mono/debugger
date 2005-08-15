using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	internal class DebuggerClientTransportSink : IClientChannelSink, IDisposable
	{
		string url;
		string channel_uri;
		string object_uri;
		DebuggerClientChannel channel;

		public DebuggerClientTransportSink (DebuggerClientChannel channel, string url)
		{
			this.channel = channel;
			this.url = url;
			channel_uri = DebuggerChannel.ParseDebuggerURL (url, out object_uri);
		}

		public IDictionary Properties {
			get { return null; }
		}

		public IClientChannelSink NextChannelSink {
			get { return null; }
		}

		public void AsyncProcessRequest (IClientChannelSinkStack sinkStack, IMessage msg,
						 ITransportHeaders requestHeaders, Stream requestStream)
		{
			if (requestHeaders == null)
				requestHeaders = new TransportHeaders();
			string request_uri = ((IMethodMessage) msg).Uri;
			requestHeaders [CommonTransportKeys.RequestUri] = request_uri;

			DebuggerConnection connection = channel.GetConnection (channel_uri);

			connection.SendAsyncMessage (requestStream, requestHeaders);
		}

		public void AsyncProcessResponse (IClientResponseChannelSinkStack sinkStack,
						  object state, ITransportHeaders headers,
						  Stream stream)
		{
			throw new NotImplementedException ();
		}

		public Stream GetRequestStream (IMessage msg, ITransportHeaders headers)
		{
			return null;
		}

		public void ProcessMessage (IMessage msg,
					    ITransportHeaders requestHeaders,
					    Stream requestStream,
					    out ITransportHeaders responseHeaders,
					    out Stream responseStream)
		{
			if (requestHeaders == null)
				requestHeaders = new TransportHeaders();
			string request_uri = ((IMethodMessage) msg).Uri;
			requestHeaders [CommonTransportKeys.RequestUri] = request_uri;

			DebuggerConnection connection = channel.GetConnection (channel_uri);

			connection.SendMessage (requestStream, requestHeaders,
						out responseHeaders, out responseStream);
		}

#region IDisposable implementation
		~DebuggerClientTransportSink ()
		{
			Dispose (false);
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				Console.Error.WriteLine ("DISPOSE CLIENT TRANSPORT SINK!");
			}

			disposed = true;
		}


		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion
	}
}	
