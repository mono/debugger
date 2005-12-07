using System;
using System.Threading;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Frontend
{
	public class CommandLineInterpreter : Interpreter
	{
		DebuggerEngine engine;
		LineParser parser;
		string prompt;
		int line = 0;

		Thread command_thread;
		Thread main_thread;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_static_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_get_pending_sigint ();

		static CommandLineInterpreter ()
		{
			mono_debugger_server_static_init ();
		}

		internal CommandLineInterpreter (bool is_synchronous, bool is_interactive,
						 DebuggerOptions options)
			: base (is_synchronous, is_interactive, options)
		{
			engine = SetupEngine ();
			parser = new LineParser (engine);

			if (is_interactive) {
				prompt = options.Prompt;
				if (options.InEmacs)
					Style = GetStyle("emacs");
			}

			main_thread = new Thread (new ThreadStart (main_thread_main));
			main_thread.IsBackground = true;

			command_thread = new Thread (new ThreadStart (command_thread_main));
			command_thread.IsBackground = true;
		}

		DebuggerEngine SetupEngine ()
		{
			DebuggerEngine e = new DebuggerEngine (this);

			e.RegisterCommand ("pwd", typeof (PwdCommand));
			e.RegisterCommand ("cd", typeof (CdCommand));
			e.RegisterCommand ("print", typeof (PrintExpressionCommand));
			e.RegisterAlias   ("p", typeof (PrintExpressionCommand));
			e.RegisterCommand ("ptype", typeof (PrintTypeCommand));
			e.RegisterCommand ("call", typeof (CallCommand));
			e.RegisterCommand ("examine", typeof (ExamineCommand));
			e.RegisterAlias   ("x", typeof (ExamineCommand));
			e.RegisterCommand ("file", typeof (FileCommand));
			e.RegisterCommand ("frame", typeof (PrintFrameCommand));
			e.RegisterAlias   ("f", typeof (PrintFrameCommand));
			e.RegisterCommand ("disassemble", typeof (DisassembleCommand));
			e.RegisterAlias   ("dis", typeof (DisassembleCommand));
			e.RegisterCommand ("process", typeof (SelectProcessCommand));
			e.RegisterCommand ("background", typeof (BackgroundProcessCommand));
			e.RegisterAlias   ("bg", typeof (BackgroundProcessCommand));
			e.RegisterCommand ("stop", typeof (StopProcessCommand));
			e.RegisterCommand ("continue", typeof (ContinueCommand));
			e.RegisterAlias   ("cont", typeof (ContinueCommand));
			e.RegisterAlias   ("c", typeof (ContinueCommand));
			e.RegisterCommand ("step", typeof (StepCommand));
			e.RegisterAlias   ("s", typeof (StepCommand));
			e.RegisterCommand ("next", typeof (NextCommand));
			e.RegisterAlias   ("n", typeof (NextCommand));
			e.RegisterCommand ("stepi", typeof (StepInstructionCommand));
			e.RegisterAlias   ("i", typeof (StepInstructionCommand));
			e.RegisterCommand ("nexti", typeof (NextInstructionCommand));
			e.RegisterAlias   ("t", typeof (NextInstructionCommand));
			e.RegisterCommand ("finish", typeof (FinishCommand));
			e.RegisterAlias   ("fin", typeof (FinishCommand));
			e.RegisterCommand ("backtrace", typeof (BacktraceCommand));
			e.RegisterAlias   ("bt", typeof (BacktraceCommand));
			e.RegisterAlias   ("where", typeof (BacktraceCommand));
			e.RegisterCommand ("up", typeof (UpCommand));
			e.RegisterCommand ("down", typeof (DownCommand));
			e.RegisterCommand ("kill", typeof (KillCommand));
			e.RegisterAlias   ("k", typeof (KillCommand));
			e.RegisterCommand ("set", typeof (SetCommand));
			e.RegisterCommand ("show", typeof (ShowCommand));
			e.RegisterCommand ("info", typeof (ShowCommand)); /* for gdb users */
			e.RegisterCommand ("threadgroup", typeof (ThreadGroupCommand));
			e.RegisterCommand ("enable", typeof (BreakpointEnableCommand));
			e.RegisterCommand ("disable", typeof (BreakpointDisableCommand));
			e.RegisterCommand ("delete", typeof (BreakpointDeleteCommand));
			e.RegisterCommand ("list", typeof (ListCommand));
			e.RegisterAlias   ("l", typeof (ListCommand));
			e.RegisterCommand ("break", typeof (BreakCommand));
			e.RegisterAlias   ("b", typeof (BreakCommand));
			e.RegisterCommand ("catch", typeof (CatchCommand));
			e.RegisterCommand ("quit", typeof (QuitCommand));
			e.RegisterAlias   ("q", typeof (QuitCommand));
			e.RegisterCommand ("dump", typeof (DumpCommand));
			e.RegisterCommand ("help", typeof (HelpCommand));
			e.RegisterCommand ("library", typeof (LibraryCommand));
			e.RegisterCommand ("run", typeof (RunCommand));
			e.RegisterAlias   ("r", typeof (RunCommand));
			e.RegisterCommand ("about", typeof (AboutCommand));
			e.RegisterCommand ("lookup", typeof (LookupCommand));
			e.RegisterCommand ("return", typeof (ReturnCommand));
			e.RegisterCommand ("save", typeof (SaveCommand));
			e.RegisterCommand ("load", typeof (LoadCommand));

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

		void main_thread_main ()
		{
			while (true) {
				try {
					DebuggerManager.ClearInterrupt ();
					MainLoop ();
				} catch (ThreadAbortException) {
					Thread.ResetAbort ();
				}
			}
		}

		public void RunMainLoop ()
		{
			command_thread.Start ();

			if (Options.StartTarget)
				Start ();

			main_thread.Start ();
			main_thread.Join ();
		}

		public string ReadInput (bool is_complete)
		{
			++line;
		again:
			string result;
			string the_prompt = is_complete ? prompt : "... ";
			if (Options.IsScript) {
				Console.Write (the_prompt);
				result = Console.ReadLine ();
				if (result == null)
					return null;
				if (result != "")
					;
				else if (is_complete) {
					engine.Repeat ();
					goto again;
				}
				return result;
			} else {
				result = GnuReadLine.ReadLine (the_prompt);
				if (result == null)
					return null;
				if (result != "")
					GnuReadLine.AddHistory (result);
				else if (is_complete) {
					engine.Repeat ();
					goto again;
				}
				return result;
			}
		}

		public void Error (int pos, string message)
		{
			if (Options.IsScript) {
				// If we're reading from a script, abort.
				Console.Write ("ERROR in line {0}, column {1}: ", line, pos);
				Console.WriteLine (message);
				Environment.Exit (1);
			}
			else {
				string prefix = new String (' ', pos + prompt.Length);
				Console.WriteLine ("{0}^", prefix);
				Console.Write ("ERROR: ");
				Console.WriteLine (message);
			}
		}

		static string GetValue (ref string[] args, ref int i, string ms_val)
		{
			if (ms_val == "")
				return null;

			if (ms_val != null)
				return ms_val;

			if (i >= args.Length)
				return null;

			return args[++i];
		}

		static bool ParseDebugFlags (DebuggerOptions options, string value)
		{
			if (value == null)
				return false;

			int pos = value.IndexOf (':');
			if (pos > 0) {
				string filename = value.Substring (0, pos);
				value = value.Substring (pos + 1);
				
				options.DebugOutput = filename;
			}
			try {
				options.DebugFlags = (DebugFlags) Int32.Parse (value);
			} catch {
				return false;
			}
			options.HasDebugFlags = true;
			return true;
		}

		static bool ParseOption (DebuggerOptions debug_options,
					 string option,
					 ref string [] args,
					 ref int i,
					 ref bool args_follow_exe)
		{
			int idx = option.IndexOf (':');
			string arg, value, ms_value = null;

			if (idx == -1){
				arg = option;
			} else {
				arg = option.Substring (0, idx);
				ms_value = option.Substring (idx + 1);
			}

			switch (arg) {
			case "-args":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				args_follow_exe = true;
				return true;

			case "-working-directory":
			case "-cd":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.WorkingDirectory = value;
				return true;

			case "-debug-flags":
				value = GetValue (ref args, ref i, ms_value);
				if (!ParseDebugFlags (debug_options, value)) {
					Usage ();
					Environment.Exit (1);
				}
				return true;

			case "-jit-optimizations":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.JitOptimizations = value;
				return true;

			case "-fullname":
			case "-f":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.InEmacs = true;
				return true;

			case "-mono-prefix":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.MonoPrefix = value;
				return true;

			case "-prompt":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.Prompt = value;
				return true;

			case "-script":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.IsScript = true;
				return true;

			case "-version":
			case "-V":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				About();
				Environment.Exit (1);
				return true;

			case "-help":
			case "--help":
			case "-h":
			case "-usage":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				Usage();
				Environment.Exit (1);
				return true;

			case "-run":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.StartTarget = true;
				return true;
			}

			return false;
		}

		static DebuggerOptions ParseCommandLine (string[] args)
		{
			DebuggerOptions options = new DebuggerOptions ();
			int i;
			bool parsing_options = true;
			bool args_follow = false;

			for (i = 0; i < args.Length; i++) {
				string arg = args[i];

				if (arg == "")
					continue;

				if (!parsing_options)
					continue;

				if (arg.StartsWith ("-")) {
					if (ParseOption (options, arg, ref args, ref i, ref args_follow))
						continue;
				} else if (arg.StartsWith ("/")) {
					string unix_opt = "-" + arg.Substring (1);
					if (ParseOption (options, unix_opt, ref args, ref i, ref args_follow))
						continue;
				}

				options.File = arg;
				break;
			}

			if (args_follow) {
				string[] argv = new string [args.Length - i];
				Array.Copy (args, i, argv, 0, args.Length - i);
				options.InferiorArgs = argv;
			} else {
				options.InferiorArgs = new string [1];
				options.InferiorArgs [0] = options.File;
			}

			return options;
		}

		static void Usage ()
		{
			Console.WriteLine (
				"Mono Debugger, (C) 2003-2004 Novell, Inc.\n" +
				"mdb [options] [exe-file]\n" +
				"mdb [options] -args exe-file [inferior-arguments ...]\n\n" +
				
				"   -args                     Arguments after exe-file are passed to inferior\n" +
				"   -debug-flags:PARAM        Sets the debugging flags\n" +
				"   -fullname                 Sets the debugging flags (short -f)\n" +
				"   -jit-optimizations:PARAM  Set jit optimizations used on the inferior process\n" +
				"   -mono-prefix:PATH         Override the mono prefix\n" +
				"   -native-symtabs           Load native symtabs\n" +
				"   -prompt:PROMPT            Override the default command-line prompt (short -p)\n" +
				"   -script                  \n" +
				"   -usage                   \n" +
				"   -version                  Display version and licensing information (short -V)\n" +
				"   -working-directory:DIR    Sets the working directory (short -cd)\n"
				);
		}

		static void About ()
		{
			Console.WriteLine (
				"The Mono Debugger is (C) 2003, 2004 Novell, Inc.\n\n" +
				"The debugger source code is released under the terms of the GNU GPL\n\n" +

				"For more information on Mono, visit the project Web site\n" +
				"   http://www.go-mono.com\n\n" +

				"The debugger was written by Martin Baulig and Chris Toshok");

			Environment.Exit (0);
		}

		void command_thread_main ()
		{
			do {
				Semaphore.Wait ();
				if (mono_debugger_server_get_pending_sigint () == 0)
					continue;

				DebuggerManager.Interrupt ();
				main_thread.Abort ();
			} while (true);
		}

		public static void Main (string[] args)
		{
			bool is_terminal = GnuReadLine.IsTerminal (0);

			DebuggerOptions options = ParseCommandLine (args);

			Console.WriteLine ("Mono Debugger");

			CommandLineInterpreter interpreter = new CommandLineInterpreter (
				true, is_terminal, options);

			try {
				interpreter.RunMainLoop ();
			} catch (Exception ex) {
				Console.WriteLine ("EX: {0}", ex);
			} finally {
				interpreter.Exit ();
			}
		}
	}
}
