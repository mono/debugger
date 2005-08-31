using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;

using Mono.Debugger;
using Mono.Debugger.Remoting;

namespace Mono.Debugger.Remoting
{
	public static class Server
	{
		static void Main (string[] args)
		{
			string url = args [0];
			DebuggerServer.Run (url);
		}
	}
}

