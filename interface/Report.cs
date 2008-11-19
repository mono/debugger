using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
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
		NUnit			= 8192,
		GUI			= 16384
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

		public static bool ParseDebugFlags (string value, out DebugFlags flags)
		{
			int int_flags = 0;
			if (Int32.TryParse (value, out int_flags)) {
				flags = (DebugFlags) int_flags;
				return true;
			}

			flags = DebugFlags.None;
			foreach (string flag in value.Split (',')) {
				switch (flag) {
				case "jit":
					flags |= DebugFlags.JitSymtab;
					break;
				case "address":
					flags |= DebugFlags.MethodAddress;
					break;
				case "threads":
					flags |= DebugFlags.Threads;
					break;
				case "signals":
					flags |= DebugFlags.Signals;
					break;
				case "eventloop":
					flags |= DebugFlags.EventLoop;
					break;
				case "wait":
					flags |= DebugFlags.Wait;
					break;
				case "sse":
					flags |= DebugFlags.SSE;
					break;
				case "notification":
					flags |= DebugFlags.Notification;
					break;
				case "mutex":
					flags |= DebugFlags.Mutex;
					break;
				case "symtab":
					flags |= DebugFlags.SymbolTable;
					break;
				case "sources":
					flags |= DebugFlags.SourceFiles;
					break;
				case "dwarf":
					flags |= DebugFlags.DwarfReader;
					break;
				case "remoting":
					flags |= DebugFlags.Remoting;
					break;
				case "nunit":
					flags |= DebugFlags.NUnit;
					break;
				case "gui":
					flags |= DebugFlags.GUI;
					break;
				default:
					return false;
				}
			}

			return true;
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
		DebugFlags flags;
		string file;
		DateTime start_time;
		bool print_to_console = true;
		StreamWriter writer;

		public ReportWriter ()
		{
			string var = Environment.GetEnvironmentVariable ("MDB_DEBUG_FLAGS");
			if (var != null) {
				int pos = var.IndexOf (':');
				if (pos > 0) {
					file = var.Substring (0, pos);
					var = var.Substring (pos + 1);
				}

				if (!Report.ParseDebugFlags (var, out flags))
					Console.WriteLine ("Invalid `MDB_DEBUG_FLAGS' environment variable.");
			}

			if (file != null)
				writer = new StreamWriter (file, true);
			else
				writer = new StreamWriter (Console.OpenStandardError ());
			writer.AutoFlush = true;
			start_time = DateTime.Now;
		}

		public ReportWriter (string file, DebugFlags flags)
		{
			this.file = file;
			this.flags = flags;

			if (file != null)
				writer = new StreamWriter (file, true);
			else
				writer = new StreamWriter (Console.OpenStandardError ());
			writer.AutoFlush = true;
		}

		public DebugFlags DebugFlags {
			get { return flags; }
		}

		public string FileName {
			get { return file; }
		}

		public bool PrintToConsole {
			get { return print_to_console; }
			set { print_to_console = value; }
		}

		protected void DoWriteLine (string message)
		{
			if (file != null) {
				TimeSpan diff = DateTime.Now - start_time;
				string timestamp = new DateTime (diff.Ticks).ToString ("mm:ss:fffffff", CultureInfo.InvariantCulture);
				writer.WriteLine ("[{0}] {1}", timestamp, message);
			} else
				writer.WriteLine ("{0}", message);
		}

		public void Debug (DebugFlags category, string message)
		{
			if (((int) category & (int) flags) == 0)
				return;

			DoWriteLine (message);
		}

		public void Print (string message)
		{
			writer.Write (message);
			if (print_to_console && (file != null))
				Console.Write (message);
		}

		public void Error (string message)
		{
			DoWriteLine (message);
			if (print_to_console && (file != null))
				Console.WriteLine (message);
		}

		public string ReadLine ()
		{
			return Console.ReadLine ();
		}
	}
}
