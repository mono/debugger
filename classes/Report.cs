using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	[Flags]
	public enum DebugFlags {
		JIT_SYMTAB		= 1,
		METHOD_ADDRESS		= 2
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
