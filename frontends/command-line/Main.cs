using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using GLib;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	public class CommandLineInterpreter
	{
		IOChannel channel;
		GnuReadLine readline;
		DebuggerBackend backend;
		ScriptingContext context;
		DebuggerTextWriter writer;
		Parser parser;

		public CommandLineInterpreter (string[] args)
		{
			channel = new IOChannel (0, false, true);
			readline = new GnuReadLine (channel, "$ ");
			backend = new DebuggerBackend ();
			writer = new ConsoleTextWriter ();
			context = new ScriptingContext (backend, writer, writer, true);
			parser = new Parser (context, "Debugger");

			if ((args != null) && (args.Length > 0))
				context.Start (args);
		}

		public void Run ()
		{
			string last_line = null;

			while (!parser.Quit) {
				string line = readline.ReadLine ();
				if (line == null)
					break;

				line = line.TrimStart (' ', '\t');
				line = line.TrimEnd (' ', '\t');
				if (line == "")
					continue;

				if (!parser.Parse (line))
					continue;

				if (line != last_line)
					readline.AddHistory (line);
				last_line = line;					
			}
		}

		public void Exit ()
		{
			backend.Dispose ();
			Environment.Exit (0);
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);
			interpreter.Run ();
			interpreter.Exit ();
		}
	}
}
