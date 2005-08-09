using System;
using System.Diagnostics;
using System.Collections;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientChannel : IChannelSender, IChannel, IDisposable
	{
		Hashtable connections;
		IClientChannelSinkProvider sink_provider;
		int priority = 1;

		public DebuggerClientChannel ()
		{
                        sink_provider = new BinaryClientFormatterSinkProvider ();
                        sink_provider.Next = new DebuggerClientTransportSinkProvider ();
			connections = new Hashtable ();
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

				if (ds != null) {
					foreach (string chnl_uri in ds.ChannelUris) {
						Console.Error.WriteLine ("CREATE MESSAGE SINK #2: {0}", chnl_uri);

						string path = chnl_uri + "!" + url;
						if (Parse (path, out objectURI) == null)
							continue;
						Console.WriteLine ("CREATE MESSAGE SINK #3: {0} {1}", path, objectURI);
						return (IMessageSink) sink_provider.CreateSink (
							this, path, remoteChannelData);
					}
				}
			}

			return null;
		}

		public string Parse (string url, out string objectURI)
		{
			Console.Error.WriteLine ("CLIENT PARSE: {0}", url);
			string host;
			string path = DebuggerChannel.ParseDebuggerURL (url, out host, out objectURI);
			return "mdb://" + host + ":" + path;
		}

		public DebuggerClientConnection GetConnection (string host, string path)
		{
			lock (this) {
				DebuggerClientConnection connection = (DebuggerClientConnection) connections [path];
				if (connection != null)
					return connection;

				string[] envp = new string [0];
				string[] argv = { "/home/martin/INSTALL/bin/mono", "--debug", path };

				connection = new DebuggerClientConnection (argv, envp);
				connections.Add (path, connection);
				return connection;
			}
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
				foreach (DebuggerClientConnection connection in connections.Values)
					connection.Dispose ();
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
