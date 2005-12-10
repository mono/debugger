using System;
using System.IO;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClient : MarshalByRefObject
	{
		public delegate void EventHandler (DebuggerClient client);

		public const bool IsRemote = true;

		static DebuggerChannel channel;
		static Hashtable connections = Hashtable.Synchronized (new Hashtable ());

		public static void GlobalShutdown ()
		{
			if (channel != null) {
				ChannelServices.UnregisterChannel (channel);
				channel = null;
			}
		}

		int id;
		string url;
		DebuggerManager manager;
		DebuggerServer server;
		Debugger backend;
		DebuggerConnection connection;
		ILease lease;
		Sponsor sponsor;

		internal DebuggerClient (DebuggerManager manager, int id, string host, string path)
		{
			this.manager = manager;
			this.id = id;

			// FIXME FIXME FIXME
			LifetimeServices.LeaseTime = TimeSpan.FromHours (3);
			LifetimeServices.LeaseManagerPollTime = TimeSpan.FromHours (3);
			LifetimeServices.RenewOnCallTime = TimeSpan.FromHours (3);

			if (channel == null) {
				channel = new DebuggerChannel ();
				ChannelServices.RegisterChannel (channel);
			}

			if (path == null)
				path = "";

			if (IsRemote) {
				connection = Connect (host, path);
				connection.ConnectionClosedEvent += client_connection_closed;
				object[] url = { new UrlAttribute (connection.URL) };
				object[] args = { manager, this };
				server = (DebuggerServer) Activator.CreateInstance (
					typeof (DebuggerServer), args, url);

				backend = server;

				lease = (ILease) server.GetLifetimeService ();
				sponsor = new Sponsor ();
				lease.Register (sponsor);
			} else {
				object[] args = { manager, this };
				AppDomain domain = AppDomain.CreateDomain ("mdb");
				server = (DebuggerServer) domain.CreateInstanceAndUnwrap (
					"Mono.Debugger", "Mono.Debugger.Remoting.DebuggerServer",
					false, 0, null, args, null, null, null);

				backend = server;
			}
		}

		public int ID {
			get { return id; }
		}

		public static DebuggerConnection GetConnection (string url)
		{
			return (DebuggerConnection) connections [url];
		}

		internal static DebuggerConnection Connect (string host, string path)
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

		public DebuggerManager DebuggerManager {
			get { return manager; }
		}

		public DebuggerServer DebuggerServer {
			get { return server; }
		}

		static void connection_closed (DebuggerConnection connection)
		{
			connections.Remove (connection.URL);
		}

		void client_connection_closed (DebuggerConnection connection)
		{
			OnClientShutdown ();
		}

		public event EventHandler ClientShutdown;

		protected void OnClientShutdown ()
		{
			if (ClientShutdown != null)
				ClientShutdown (this);
		}

		[OneWay]
		public void Shutdown ()
		{
			lock (this) {
				if (connection != null) {
					lease.Unregister (sponsor);
					connection.Shutdown ();
					connection = null;
				}
			}
		}

		[Serializable]
		protected class Sponsor : ISponsor
		{
			public TimeSpan Renewal (ILease lease)
			{
				return TimeSpan.FromHours (3);
			}
		}
	}
}
