using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	public class CommandLineInterpreter
	{
		GnuReadLine readline = null;
		DebuggerBackend backend;
		ScriptingContext context;
		DebuggerTextWriter writer;
		Parser parser;

		public CommandLineInterpreter (string[] args)
		{
			if (GnuReadLine.IsTerminal (0)) {
				readline = new GnuReadLine ("$ ");
			}

			backend = new DebuggerBackend ();
			writer = new ConsoleTextWriter ();
			context = new ScriptingContext (backend, writer, writer, true, readline != null);
			parser = new Parser (context, "Debugger");

			if ((args != null) && (args.Length > 0))
				context.Start (args);
		}

		public void Run ()
		{
			string last_line = null;

			while (!parser.Quit) {
				string line;
				if (readline != null)
					line = readline.ReadLine ();
				else
					line = Console.ReadLine ();
				if (line == null)
					break;

				line = line.TrimStart (' ', '\t');
				line = line.TrimEnd (' ', '\t');
				if (line == "")
					continue;

				if (!parser.Parse (line))
					continue;

				if ((readline != null) && (line != last_line))
					readline.AddHistory (line);
				last_line = line;					
			}
		}

		public void Exit ()
		{
			backend.Dispose ();
			Environment.Exit (context.ExitCode);
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);
			interpreter.Run ();
			interpreter.Exit ();
		}
	}
}
