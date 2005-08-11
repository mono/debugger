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
		DebuggerServerChannel server_channel;
		IClientChannelSinkProvider sink_provider;
		DebuggerConnection server_connection;
		int priority = 1;

		public DebuggerClientChannel (DebuggerServerChannel server_channel)
		{
                        sink_provider = new BinaryClientFormatterSinkProvider ();
                        sink_provider.Next = new DebuggerClientTransportSinkProvider ();
			this.server_channel = server_channel;
			connections = new Hashtable ();
		}

		public DebuggerClientChannel (DebuggerServerChannel server_channel,
					      DebuggerConnection server_connection)
			: this (server_channel)
		{
			this.server_connection = server_connection;
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
			string host, the_path;
			if (DebuggerChannel.ParseDebuggerURL (url, out host, out the_path, out objectURI) != null)
				return (IMessageSink) sink_provider.CreateSink (this, url, remoteChannelData);

			DebuggerServerChannelData data = remoteChannelData as DebuggerServerChannelData;
			if (data != null) {
				string path = data.ChannelURL + "!" + url;
				return (IMessageSink) sink_provider.CreateSink (this, path, remoteChannelData);
			}

			return null;
		}

		public string Parse (string url, out string objectURI)
		{
			string host, path;
			return DebuggerChannel.ParseDebuggerURL (url, out host, out path, out objectURI);
		}

		public DebuggerConnection GetConnection (string channel_uri, string host, string path)
		{
			lock (this) {
				if (server_connection != null)
					return server_connection;

				DebuggerConnection connection = (DebuggerConnection) connections [channel_uri];
				if (connection != null)
					return connection;

				ArrayList list = new ArrayList ();
				IDictionary env_vars = System.Environment.GetEnvironmentVariables ();
				foreach (string var in env_vars.Keys) {
					list.Add (String.Format ("{0}={1}", var, env_vars [var]));
				}

				string[] envp = new string [list.Count];
				list.CopyTo (envp);

				if (host == null)
					host = "";

				string default_wrapper = Mono.Debugger.AssemblyInfo.libdir +
						System.IO.Path.DirectorySeparatorChar + "mono" +
						System.IO.Path.DirectorySeparatorChar + "1.0" +
						System.IO.Path.DirectorySeparatorChar + "mdb-server";

				string wrapper_path = path;
				if (path == "")
					wrapper_path = default_wrapper;

				string[] argv;
				if (host == "")
					argv = new string[] { wrapper_path, channel_uri, host, wrapper_path };
				else
					argv = new string[] { default_wrapper, channel_uri, host, wrapper_path };

				connection = new DebuggerConnection (server_channel, argv, envp);
				connections.Add (channel_uri, connection);
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
				foreach (DebuggerConnection connection in connections.Values)
					((IDisposable) connection).Dispose ();
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
