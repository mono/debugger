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
			try {
				ok = parser.Parse (line);
			} catch {
				return true;
			}

			return !parser.Quit;
		}
	}
}

