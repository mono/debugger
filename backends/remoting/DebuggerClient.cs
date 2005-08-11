using System;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Lifetime;

namespace Mono.Debugger.Remoting
{
	public static class DebuggerClient
	{
		static DebuggerChannel channel;

		static DebuggerClient ()
		{
			channel = new DebuggerChannel ();
			ChannelServices.RegisterChannel (channel);
		}

		public static DebuggerBackend CreateConnection (string host, string remote_mono)
		{
			if (remote_mono == null)
				remote_mono = "";

			string url;
			if (host != null)
				url = "mdb://" + host + ":" + remote_mono + "!DebuggerBackend";
			else
				url = "mdb://" + remote_mono + "!DebuggerBackend";

			DebuggerBackend backend;
#if FIXME
			object[] url_arg = new object[] { new UrlAttribute (url) };
			backend = (DebuggerBackend) Activator.CreateInstance (
				typeof (DebuggerBackend), null, url_arg);
#else
			backend = (DebuggerBackend) Activator.GetObject (typeof (DebuggerBackend), url);
#endif

			ILease lease = (ILease) backend.GetLifetimeService ();
			lease.Register (new Sponsor ());

			return backend;
		}

		public static void Shutdown ()
		{
			ChannelServices.UnregisterChannel (channel);
			((IDisposable) channel).Dispose ();
			channel = null;
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
