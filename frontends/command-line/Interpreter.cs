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
		TextWriter stdout, stderr;
		ScriptingContext context;
		Parser parser;

		// <summary>
		//   Create a new command interpreter for the debugger backend @backend.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (DebuggerBackend backend, TextWriter stdout, TextWriter stderr)
		{
			this.backend = backend;
			this.stdout = stdout;
			this.stderr = stderr;

			context = new ScriptingContext (backend, stdout, stderr, false);
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
