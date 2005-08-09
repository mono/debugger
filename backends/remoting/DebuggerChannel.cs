using System;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace Mono.Debugger.Remoting
{
	public static class DebuggerChannel
	{
		internal static string ParseDebuggerURL (string url, out string host, out string objectURI)
		{
			objectURI = null;
			host = null;

			if (!url.StartsWith ("mdb://"))
				return null;

			int pos = url.IndexOf ('!', 6);
			if (pos == -1) return null;
			string path = url.Substring (6, pos - 6);

			objectURI = url.Substring (pos + 1);

			int colon = path.IndexOf (':');
			if (colon > 0) {
				host = path.Substring (0, colon);
				path = path.Substring (colon + 1);
			}

			return path;
		}
	}
}
