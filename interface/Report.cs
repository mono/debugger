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
		SourceFiles		= 1024,
		DwarfReader             = 2048,
		Remoting		= 4096,
		NUnit			= 8192
	}

	public static class Report
	{
		static ReportWriter writer;

		public static ReportWriter ReportWriter {
			get { return writer; }
		}

		public static void Initialize ()
		{
			writer = new ReportWriter ();
		}

		public static void Initialize (ReportWriter the_writer)
		{
			writer = the_writer;
		}

		public static void Initialize (string file, DebugFlags flags)
		{
			writer = new ReportWriter (file, flags);
		}

		[Conditional("DEBUG")]
		public static void Debug (DebugFlags category, object argument)
		{
			Debug (category, "{0}", argument);
		}

		[Conditional("DEBUG")]
		public static void Debug (DebugFlags category, string message, params object[] args)
		{
			string formatted = String.Format (message, args);
			ReportWriter.Debug (category, formatted);
		}

		public static void Print (string message, params object[] args)
		{
			string formatted = String.Format (message, args);
			ReportWriter.Print (formatted);
		}

		public static void Error (string message, params object[] args)
		{
			string formatted = String.Format (message, args);
			ReportWriter.Error (formatted);
		}

		public static string ReadLine ()
		{
			return ReportWriter.ReadLine ();
		}
	}

	public class ReportWriter : DebuggerMarshalByRefObject
	{
		int flags;
		string file;
		bool print_to_console = true;
		StreamWriter writer;

		public ReportWriter ()
		{
			file = Environment.GetEnvironmentVariable ("MDB_DEBUG_OUTPUT");
			if (file != null)
				writer = new StreamWriter (file, true);
			else
				writer = new StreamWriter (Console.OpenStandardError ());
			writer.AutoFlush = true;

			string var = Environment.GetEnvironmentVariable ("MDB_DEBUG_FLAGS");
			if (var != null) {
				try {
					flags = Int32.Parse (var);
				} catch {
					Console.WriteLine (
						"Invalid `MDB_DEBUG_FLAGS' environment " +
						"variable.");
				}
			}
		}

		public ReportWriter (string file, DebugFlags flags)
		{
			this.file = file;
			this.flags = (int) flags;

			if (file != null)
				writer = new StreamWriter (file, true);
			else
				writer = new StreamWriter (Console.OpenStandardError ());
			writer.AutoFlush = true;
		}

		public bool PrintToConsole {
			get { return print_to_console; }
			set { print_to_console = value; }
		}

		public void Debug (DebugFlags category, string message)
		{
			if (((int) category & (int) flags) == 0)
				return;

			writer.WriteLine (message);
		}

		public void Print (string message)
		{
			writer.Write (message);
			if (print_to_console && (file != null))
				Console.Write (message);
		}

		public void Error (string message)
		{
			writer.Write (message);
			if (print_to_console && (file != null))
				Console.Write (message);
		}

		public string ReadLine ()
		{
			return Console.ReadLine ();
		}
	}
}