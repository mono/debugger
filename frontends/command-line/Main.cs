using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Mono.Debugger;
using Mono.Debugger.Frontends.Scripting;
using CL;

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
	public class CommandLineInterpreter : Interpreter
	{
		GnuReadLine readline = null;
		DebuggerEngine engine;
		LineParser parser;
		string default_prompt, prompt;
		string last_line = "";
		int line = 0;

		internal CommandLineInterpreter (DebuggerTextWriter command_out,
						 DebuggerTextWriter inferior_out,
						 bool is_synchronous, bool is_interactive,
						 string[] args)
			: base (command_out, inferior_out, is_synchronous, is_interactive,
				args)
		{
			engine = SetupEngine ();
			parser = new LineParser (engine);

			if (is_interactive) {
				prompt = default_prompt = "$ ";
				readline = new GnuReadLine ();
			}
		}

		DebuggerEngine SetupEngine ()
		{
			DebuggerEngine e = new DebuggerEngine (GlobalContext);

			Type command_type = typeof (Command);

			foreach (Type type in command_type.Assembly.GetTypes ()) {
				if (!type.IsSubclassOf (command_type) || type.IsAbstract)
					continue;

				object[] attrs = type.GetCustomAttributes (
					typeof (CommandAttribute), false);

				if (attrs.Length != 1) {
					Console.WriteLine ("Ignoring command `{0}'", type);
					continue;
				}

				CommandAttribute attr = (CommandAttribute) attrs [0];
				e.Register (attr.Name, type);
			}

			return e;
		}

		public void MainLoop ()
		{
			string s;
			bool is_complete = true;

			parser.Reset ();
			while ((s = ReadInput (is_complete)) != null) {
				parser.Append (s);
				if (parser.IsComplete ()){
					parser.Execute ();
					parser.Reset ();
					is_complete = true;
				} else
					is_complete = false;
			}
		}

		public string ReadInput (bool is_complete)
		{
			++line;
			if (readline != null) {
				string prompty = is_complete ? "(mdb) " : "... ";
				string result = readline.ReadLine (prompt);
				if (result == null)
					return null;
				if (result != "")
					readline.AddHistory (result);
				else
					result = last_line;
				last_line = result;				
				return result;
			} else
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

		public static void Main (string[] args)
		{
			ConsoleTextWriter writer = new ConsoleTextWriter ();

			bool is_terminal = GnuReadLine.IsTerminal (0);

			CommandLineInterpreter interpreter = new CommandLineInterpreter (
				writer, writer, true, is_terminal, args);

			Console.WriteLine ("Mono debugger");
			try {
				interpreter.MainLoop ();
			} catch (Exception ex) {
				Console.WriteLine ("EX: {0}", ex);
			} finally {
				interpreter.Exit ();
			}
		}
	}
}
