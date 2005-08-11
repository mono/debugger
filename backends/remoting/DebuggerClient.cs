using System;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;

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

		public static DebuggerBackend CreateConnection ()
		{
			string base_directory = System.AppDomain.CurrentDomain.BaseDirectory;

			/* Use relative path based on where Mono.Debugger.Remoting.dll is at to enable relocation */
			string path = Path.GetFullPath (
				base_directory + Path.DirectorySeparatorChar + "mdb-server.exe");
			string url = "mdb://" + Environment.MachineName + ":" + path + "!DebuggerBackend";

			DebuggerBackend backend;
#if FIXME
			object[] url_arg = new object[] { new UrlAttribute (url) };
			backend = (DebuggerBackend) Activator.CreateInstance (
				typeof (DebuggerBackend), null, url_arg);
#else
			backend = (DebuggerBackend) Activator.GetObject (typeof (DebuggerBackend), url);
#endif

			return backend;
		}

		public static void Shutdown ()
		{
			ChannelServices.UnregisterChannel (channel);
			((IDisposable) channel).Dispose ();
			channel = null;
		}
	}
}
