using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	/// <summary>
	///   This is a very simple command-line interpreter for the Mono Debugger.
	/// </summary>
	public class Interpreter
	{
		Parser parser;
		string last_command = "";

		// <summary>
		//   Create a new command interpreter.
		// </summary>
		public Interpreter (ScriptingContext context)
		{
			parser = new Parser (context, "Debugger");
		}

		public bool ProcessCommand (string line)
		{
			bool ok;
			if (line == "")
				line = last_command;
			if (line.StartsWith ("list"))
				line = "list continue";
			line = line.TrimStart (' ', '\t');
			line = line.TrimEnd (' ', '\t');
			if (line == "")
				return false;

			try {
				ok = parser.Parse (line);
				last_command = line;
			} catch {
				return true;
			}

			return !parser.Quit;
		}
	}
}

