using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Mono.Debugger;

//
// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
//
[assembly: AssemblyTitle("Interpreter")]
[assembly: AssemblyDescription("Mono Debugger - Command Line Interpreter")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("")]
[assembly: AssemblyCopyright("(C) 2003 Ximian, Inc.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]		

[assembly: Mono.About("Distributed under the GPL")]

[assembly: Mono.UsageComplement("application args")]

[assembly: Mono.Author("Martin Baulig")]
[assembly: Mono.Author("Miguel de Icaza")]

//
// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Revision and Build Numbers 
// by using the '*' as shown below:

[assembly: AssemblyVersion("0.2.*")]


namespace Mono.Debugger.Frontends.CommandLine
{
	public class CommandLineInterpreter
	{
		GnuReadLine readline = null;
		ScriptingContext context;
		DebuggerTextWriter writer;
		Parser parser;

		public CommandLineInterpreter (string[] args)
		{
			bool is_terminal = GnuReadLine.IsTerminal (0);

			writer = new ConsoleTextWriter ();
			context = new ScriptingContext (writer, writer, true, is_terminal);
			parser = new Parser (context, "Debugger");

			if ((args != null) && (args.Length > 0)) {
				try {
					context.Start (args);
				} catch (ScriptingException ex) {
					Console.WriteLine (ex.Message);
					Environment.Exit (255);
				}
			}

			if (is_terminal)
				readline = new GnuReadLine (context.Prompt + " ");
		}

		public void Run ()
		{
			string last_line = "";

			while (!parser.Quit) {
				string line;
				if (readline != null)
					line = readline.ReadLine ();
				else
					line = Console.ReadLine ();
				if (line == null)
					break;
				else if (line == "") {
					line = last_line;
					if (line.StartsWith ("list"))
						line = "list continue";
				}

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
			Environment.Exit (context.ExitCode);
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);

			Console.WriteLine ("Mono debugger");
			interpreter.Run ();
			interpreter.Exit ();
		}
	}
}
