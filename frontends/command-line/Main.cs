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
		GnuReadLineReader reader;
		ScriptingContext context;
		Parser parser;

		public CommandLineInterpreter (string[] args)
		{
			reader = new GnuReadLineReader (new GnuReadLine ("$ "));
			context = new ScriptingContext ();
			parser = new Parser (context, reader, "Debugger", args);
		}

		public void Run ()
		{
			while (!parser.Quit)
				parser.parse ();
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);
			interpreter.Run ();
			Environment.Exit (0);
		}
	}
}
