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

namespace Mono.Debugger.Frontends.CommandLine
{
	public class CommandLineInterpreter : Interpreter
	{
		GnuReadLine readline = null;
		DebuggerEngine engine;
		LineParser parser;
		string prompt;
		int line = 0;

		internal CommandLineInterpreter (DebuggerTextWriter command_out,
						 DebuggerTextWriter inferior_out,
						 bool is_synchronous, bool is_interactive,
						 DebuggerOptions options)
			: base (command_out, inferior_out, is_synchronous, is_interactive,
				options)
		{
			engine = SetupEngine ();
			parser = new LineParser (engine);

			if (is_interactive) {
				prompt = options.Prompt;
				if (!options.IsScript)
					readline = new GnuReadLine ();
				if (options.InEmacs)
					Style = GetStyle("emacs");
			}
		}

		DebuggerEngine SetupEngine ()
		{
			DebuggerEngine e = new DebuggerEngine (this);

			e.Register ("print", typeof (PrintExpressionCommand));
			e.Register ("p", typeof (PrintExpressionCommand));
			e.Register ("ptype", typeof (PrintTypeCommand));
			e.Register ("call", typeof (CallCommand));
			e.Register ("style", typeof (StyleCommand));
			e.Register ("examine", typeof (ExamineCommand));
			e.Register ("x", typeof (ExamineCommand));
			e.Register ("frame", typeof (PrintFrameCommand));
			e.Register ("f", typeof (PrintFrameCommand));
			e.Register ("disassemble", typeof (DisassembleCommand));
			e.Register ("dis", typeof (DisassembleCommand));
			e.Register ("process", typeof (SelectProcessCommand));
			e.Register ("background", typeof (BackgroundProcessCommand));
			e.Register ("bg", typeof (BackgroundProcessCommand));
			e.Register ("stop", typeof (StopProcessCommand));
			e.Register ("continue", typeof (ContinueCommand));
			e.Register ("c", typeof (ContinueCommand));
			e.Register ("step", typeof (StepCommand));
			e.Register ("s", typeof (StepCommand));
			e.Register ("next", typeof (NextCommand));
			e.Register ("n", typeof (NextCommand));
			e.Register ("stepi", typeof (StepInstructionCommand));
			e.Register ("i", typeof (StepInstructionCommand));
			e.Register ("nexti", typeof (NextInstructionCommand));
			e.Register ("t", typeof (NextInstructionCommand));
			e.Register ("finish", typeof (FinishCommand));
			e.Register ("backtrace", typeof (BacktraceCommand));
			e.Register ("bt", typeof (BacktraceCommand));
			e.Register ("where", typeof (BacktraceCommand));
			e.Register ("up", typeof (UpCommand));
			e.Register ("down", typeof (DownCommand));
			e.Register ("kill", typeof (KillCommand));
			e.Register ("show", typeof (ShowCommand));
			e.Register ("threadgroup", typeof (ThreadGroupCommand));
			e.Register ("enable", typeof (BreakpointEnableCommand));
			e.Register ("disable", typeof (BreakpointDisableCommand));
			e.Register ("delete", typeof (BreakpointDeleteCommand));
			e.Register ("list", typeof (ListCommand));
			e.Register ("break", typeof (BreakCommand));
			e.Register ("b", typeof (BreakCommand));
			e.Register ("quit", typeof (QuitCommand));
			e.Register ("q", typeof (QuitCommand));
			e.Register ("dump", typeof (DumpCommand));
			e.Register ("help", typeof (HelpCommand));
			e.Register ("library", typeof (LibraryCommand));
			e.Register ("save", typeof (SaveCommand));
			e.Register ("load", typeof (LoadCommand));
			e.Register ("restart", typeof (RestartCommand));
			e.Register ("run", typeof (RunCommand));
			e.Register ("r", typeof (RunCommand));
			e.Register ("about", typeof (AboutCommand));
			e.Register ("start", typeof (StartCommand));
			e.Register ("lookup", typeof (LookupCommand));
			e.Register ("unwind", typeof (UnwindCommand));

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
			again:
				string the_prompt = is_complete ? prompt : "... ";
				string result = readline.ReadLine (the_prompt);
				if (result == null)
					return null;
				if (result != "")
					readline.AddHistory (result);
				else if (is_complete) {
					engine.Repeat ();
					goto again;
				}
				return result;
			} else {
				if (prompt != null)
					Console.Write (prompt);
				return Console.ReadLine ();
			}
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

			DebuggerOptions options = new DebuggerOptions ();
			options.ProcessArgs (args);
			if (options.DebugFlags != 0)
				Report.CurrentDebugFlags = options.DebugFlags;

			Console.WriteLine ("Mono Debugger");

			CommandLineInterpreter interpreter = new CommandLineInterpreter (
				writer, writer, true, is_terminal, options);

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
