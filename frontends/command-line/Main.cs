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
	public class CommandLineInterpreter : InputProvider
	{
		GnuReadLine readline = null;
		ScriptingContext context;
		DebuggerTextWriter writer;
		Engine engine;
		Parser parser;
		string default_prompt, prompt;
		int line = 0;

		public CommandLineInterpreter (string[] args)
		{
			bool is_terminal = GnuReadLine.IsTerminal (0);

			writer = new ConsoleTextWriter ();
			context = new ScriptingContext (
				writer, writer, true, is_terminal, args);

			engine = new Engine (context);
			parser = new Parser (engine, this);

			if (is_terminal) {
				prompt = default_prompt = context.Prompt + " ";
				readline = new GnuReadLine ();
			}

			if (!context.HasBackend)
				return;

			try {
				context.Run ();
			} catch (TargetException e) {
				Console.WriteLine ("Cannot start target: {0}", e.Message);
			}
		}

		public string ReadInput ()
		{
			++line;
			if (readline != null)
				return readline.ReadLine (prompt);
			else
				return Console.ReadLine ();
		}

		public string ReadMoreInput ()
		{
			++line;
			if (readline != null)
				return readline.ReadLine (".. ");
			else
				return Console.ReadLine ();
		}

		public void Error (int pos, string message)
		{
			if (readline != null) {
				string prefix = new String (' ', pos + prompt.Length);
				Console.WriteLine ("{0}^", prefix);
				Console.Write ("ERROR: ");
				Console.WriteLine (message);
			} else {
				// If we're reading from a script, abort.
				Console.Write ("ERROR in line {0}, column {1}: ", line, pos);
				Console.WriteLine (message);
				Environment.Exit (1);
			}
		}

		public void Run ()
		{
			parser.Run ();
		}

		public void Exit ()
		{
			context.Exit ();
			Environment.Exit (context.ExitCode);
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);

			Console.WriteLine ("Mono debugger");
			try {
				interpreter.Run ();
			} catch (Exception ex) {
				Console.WriteLine ("EX: {0}", ex);
			} finally {
				interpreter.Exit ();
			}
		}
	}
}
