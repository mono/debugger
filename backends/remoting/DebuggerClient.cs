using System;
using System.IO;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Lifetime;

namespace Mono.Debugger.Remoting
{
	[Serializable]
	public class DebuggerClient
	{
		static DebuggerChannel channel;
		static Hashtable connections = Hashtable.Synchronized (new Hashtable ());

		public static void GlobalShutdown ()
		{
			if (channel != null) {
				ChannelServices.UnregisterChannel (channel);
				channel = null;
			}
		}

		string url;
		DebuggerServer server;
		DebuggerConnection connection;
		ILease lease;
		Sponsor sponsor;

		public DebuggerClient (string host, string remote_mono)
		{
			if (channel == null) {
				channel = new DebuggerChannel ();
				ChannelServices.RegisterChannel (channel);
			}

			if (remote_mono == null)
				remote_mono = "";

			connection = Connect (host, remote_mono);
			string url = connection.URL + "!DebuggerServer";

			server = (DebuggerServer) Activator.GetObject (typeof (DebuggerServer), url);

			lease = (ILease) server.GetLifetimeService ();
			sponsor = new Sponsor ();
			lease.Register (sponsor);
		}

		public static DebuggerConnection GetConnection (string url)
		{
			return (DebuggerConnection) connections [url];
		}

		public static DebuggerConnection Connect (string host, string path)
		{
			string guid = Guid.NewGuid ().ToString ();
			string channel_uri = "mdb://" + guid;

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

			DebuggerConnection connection = new DebuggerConnection (
				channel.ServerChannel, channel_uri, argv, envp);
			connection.ConnectionClosedEvent += new ConnectionHandler (connection_closed);
			connections.Add (channel_uri, connection);
			return connection;
		}

		public DebuggerServer DebuggerServer {
			get { return server; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return server.DebuggerBackend; }
		}

		static void connection_closed (DebuggerConnection connection)
		{
			connections.Remove (connection.URL);
		}

		public void Shutdown ()
		{
			lease.Unregister (sponsor);
			connection.Shutdown ();
		}

		[Serializable]
		protected class Sponsor : ISponsor
		{
			public 	TimeSpan Renewal (ILease lease)
			{
				return new TimeSpan (0, 5, 0);
			}
		}
	}
}
