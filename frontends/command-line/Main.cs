using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	public class CommandLineInterpreter
	{
		DebuggerBackend backend;
		Interpreter interpreter;
		GnuReadLine readline;

		public CommandLineInterpreter (string[] args)
		{
			backend = new DebuggerBackend ();
			interpreter = new Interpreter (backend, Console.Out, Console.Error);
			readline = new GnuReadLine ();

			interpreter.PrintCurrentInsn = true;

			if (args.Length > 0)
				LoadProgram (args);

			backend.FrameChangedEvent += new StackFrameHandler (frame_changed);
			backend.StateChanged += new StateChangedHandler (state_changed);
			backend.TargetOutput += new TargetOutputHandler (inferior_output);
			backend.TargetError += new TargetOutputHandler (inferior_error);
			backend.DebuggerOutput += new TargetOutputHandler (debugger_output);
			backend.DebuggerError += new DebuggerErrorHandler (debugger_error);
		}

		void LoadProgram (string[] args)
		{
			if (args [0] == "core") {
				string [] program_args = new string [args.Length-2];
				if (args.Length > 2)
					Array.Copy (args, 2, program_args, 0, args.Length-2);

				LoadProgram (args [1], program_args);
				backend.ReadCoreFile ("thecore");
			} else{
				string [] program_args = new string [args.Length-1];
				if (args.Length > 1)
					Array.Copy (args, 1, program_args, 0, args.Length-1);

				LoadProgram (args [0], program_args);
			}
		}

		void LoadProgram (string program, string [] args)
		{
			Console.WriteLine ("Loading program: {0}", program);
			backend.CommandLineArguments = args;
			backend.TargetApplication = program;
		}

		bool ProcessCommand (string line)
		{
			try {
				return interpreter.ProcessCommand (line, false);
			} catch (Exception e) {
				Console.WriteLine (e);
				return true;
			}
		}

		void state_changed (TargetState state, int arg)
		{
			switch (state) {
			case TargetState.EXITED:
				if (arg == 0)
					Console.WriteLine ("Program terminated normally.");
				else
					Console.WriteLine ("Program exited with exit code {0}.", arg);
				break;

			case TargetState.STOPPED:
				if (arg != 0)
					Console.WriteLine ("Program received signal {0}.", arg);
				break;
			}
		}

		void inferior_output (string line)
		{
			Console.WriteLine ("INFERIOR OUTPUT: {0}", line);
		}

		void inferior_error (string line)
		{
			Console.WriteLine ("INFERIOR ERROR: {0}", line);
		}

		void debugger_output (string line)
		{
			Console.WriteLine ("DEBUGGER OUTPUT: {0}", line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			Console.WriteLine ("DEBUGGER ERROR: {0}\n{1}", message, e);
		}

		void frame_changed (StackFrame frame)
		{
			interpreter.PrintFrame (frame, -1, true);
		}

		public void Run ()
		{
			string line, last_command = null;
			while ((line = readline.ReadLine ("$ ")) != null) {
				line = line.TrimStart (' ', '\t');
				line = line.TrimEnd (' ', '\t');
				if (line == "") {
					if (interpreter.CanRepeatLastCommand)
						line = last_command;
					else
						continue;
				}

				if (!ProcessCommand (line))
					break;

				if (line != last_command)
					readline.AddHistory (line);
					
				last_command = line;
			}
		}

		public static void Main (string[] args)
		{
			CommandLineInterpreter interpreter = new CommandLineInterpreter (args);
			interpreter.Run ();
			Environment.Exit (0);
		}
	}
}
