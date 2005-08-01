using System;
using System.Diagnostics;
using System.Collections;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientChannel : IChannelSender, IChannel, IDisposable
	{
		IClientChannelSinkProvider sink_provider;
		int priority = 1;

		public DebuggerClientChannel ()
		{
                        sink_provider = new BinaryClientFormatterSinkProvider ();
                        sink_provider.Next = new DebuggerClientTransportSinkProvider ();
		}

		public string ChannelName {
			get { return "mdb"; }
		}

		public int ChannelPriority {
			get { return priority; }
		}

		public IMessageSink CreateMessageSink (string url,
						       object remoteChannelData,
						       out string objectURI)
	        {
			Console.Error.WriteLine ("CREATE MESSAGE SINK: {0} {1} {2}", url, remoteChannelData,
						 sink_provider);

			string host;
			if (DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI) != null)
				return (IMessageSink) sink_provider.CreateSink (this, url, remoteChannelData);

			if (remoteChannelData != null) {
				IChannelDataStore ds = remoteChannelData as IChannelDataStore;
				Console.Error.WriteLine ("CREATE MESSAGE SINK #1: {0}", ds);
				if (ds != null && ds.ChannelUris.Length > 0)
					url = ds.ChannelUris [0];
				else {
					objectURI = null;
					return null;
				}
				Console.Error.WriteLine ("CREATE MESSAGE SINK #2: {0}", url);
			}

			if (DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI) == null)
				return null;

			return (IMessageSink) sink_provider.CreateSink (this, url, remoteChannelData);
		}

		public string Parse (string url, out string objectURI)
		{
			Console.Error.WriteLine ("CLIENT PARSE: {0}", url);
			string host;
			return DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI);
		}

#region IDisposable implementation
		~DebuggerClientChannel ()
		{
			Dispose (false);
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				Console.Error.WriteLine ("DISPOSE CLIENT CHANNEL!");
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
