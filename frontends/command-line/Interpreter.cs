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
		DebuggerTextWriter command_output, inferior_output;
		ScriptingContext context;
		Parser parser;

		// <summary>
		//   Create a new command interpreter.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (DebuggerTextWriter command_output, DebuggerTextWriter inferior_output)
		{
			this.command_output = command_output;
			this.inferior_output = inferior_output;

			context = new ScriptingContext (command_output, inferior_output, false, true);
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

		public ScriptingContext Context {
			get { return context; }
		}
	}
}

