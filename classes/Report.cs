using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	[Flags]
	public enum DebugFlags {
		None			= 0,
		JitSymtab		= 1,
		MethodAddress		= 2,
		Threads			= 4,
		Signals			= 8,
		EventLoop		= 16,
		Wait			= 32,
		SSE			= 64,
		Notification		= 128,
		Mutex			= 256,
		SymbolTable		= 512,
		SourceFiles		= 1024
	}

	public class Report
	{
		public static int CurrentDebugFlags = 0;

		static StreamWriter writer;

		static Report ()
		{
			initialize ();
		}

		[Conditional("DEBUG")]
		static void initialize ()
		{
			string file = Environment.GetEnvironmentVariable ("MDB_DEBUG_OUTPUT");
			if (file != null)
				writer = new StreamWriter (file, true);
			else
				writer = new StreamWriter (Console.OpenStandardError ());
			writer.AutoFlush = true;

			string var = Environment.GetEnvironmentVariable ("MDB_DEBUG_FLAGS");
			if (var != null) {
				try {
					CurrentDebugFlags = Int32.Parse (var);
				} catch {
					Console.WriteLine (
						"Invalid `MDB_DEBUG_FLAGS' environment " +
						"variable.");
				}
			}
		}

		[Conditional("DEBUG")]
		public static void Debug (DebugFlags category, object argument)
		{
			Debug (category, "{0}", argument);
		}

		[Conditional("DEBUG")]
		public static void Debug (DebugFlags category, string message, params object[] args)
		{
			if (((int) category & (int) CurrentDebugFlags) == 0)
				return;

			writer.WriteLine (message, args);
		}
	}
}
