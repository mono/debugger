using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	[Flags]
	public enum DebugFlags {
		JitSymtab		= 1,
		MethodAddress		= 2,
		Threads			= 4,
		Signals			= 8,
		EventLoop		= 16,
		Wait			= 32,
		SSE			= 64,
		Notification		= 128,
		Mutex			= 256
	}

	public class Report
	{
		public static int CurrentDebugFlags = 0;

		public static void Debug (DebugFlags category, object argument)
		{
			Debug (category, "{0}", argument);
		}

		public static void Debug (DebugFlags category, string message, params object[] args)
		{
			if (((int) category & (int) CurrentDebugFlags) == 0)
				return;

			Console.WriteLine (message, args);
		}
	}
}
