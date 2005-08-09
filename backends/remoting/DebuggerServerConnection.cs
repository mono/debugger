using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Remoting
{
	public static class DebuggerServerConnection
	{
		public delegate void ConnectionHandler (Stream stream);
		delegate void PollHandler ();

		static DebuggerStream stream;

		[DllImport("monodebuggerremoting")]
		static extern bool mono_debugger_remoting_poll (int fd, PollHandler func);

		public static event ConnectionHandler HandleConnection;

		static void poll_cb ()
		{
			if (HandleConnection != null)
				HandleConnection (stream);
		}

		public static void Start ()
		{
			stream = new DebuggerStream (3);
			mono_debugger_remoting_poll (3, poll_cb);
			stream.Close ();
			stream = null;
		}
	}
}
