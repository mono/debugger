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
		DebuggerBackend backend;
		DebuggerTextWriter command_output, inferior_output;
		ScriptingContext context;
		Parser parser;

		// <summary>
		//   Create a new command interpreter for the debugger backend @backend.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (DebuggerBackend backend, DebuggerTextWriter command_output,
				    DebuggerTextWriter inferior_output)
		{
			this.backend = backend;
			this.command_output = command_output;
			this.inferior_output = inferior_output;

			context = new ScriptingContext (backend, command_output, inferior_output, false);
			parser = new Parser (context, "Debugger", new string [0]);
		}

		public bool ProcessCommand (string line)
		{
			bool ok;
			try {
				ok = parser.Parse (line);
			} catch {
				return true;
			}

			return true;
		}
	}
}
