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
		ScriptingContext context;
		Parser parser;

		public CommandLineInterpreter (string[] args)
		{
			channel = new IOChannel (0, false, true);
			readline = new GnuReadLine (channel, "$ ");
			context = new ScriptingContext ();
			parser = new Parser (context, "Debugger", args);
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

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);
			interpreter.Run ();
			Environment.Exit (0);
		}
	}
}
