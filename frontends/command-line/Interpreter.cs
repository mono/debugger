using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontends.CommandLine
{
	/// <summary>
	///   This is a very simple command-line interpreter for the Mono Debugger.
	/// </summary>
	public class Interpreter
	{
		IDebuggerBackend backend;
		TextWriter stdout, stderr;
		string last_command;
		string[] last_args;

		// <summary>
		//   Create a new command interpreter for the debugger backend @backend.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (IDebuggerBackend backend, TextWriter stdout, TextWriter stderr)
		{
			this.backend = backend;
			this.stdout = stdout;
			this.stderr = stderr;
		}

		public void ShowHelp ()
		{
			stderr.WriteLine ("Commands:");
			stderr.WriteLine ("  q, quit,exit     Quit the debugger");
			stderr.WriteLine ("  r, run           Start/continue the target");
			stderr.WriteLine ("  abort            Abort the target");
			stderr.WriteLine ("  kill             Kill the target");
			stderr.WriteLine ("  b, break-method  Add breakpoint for a CSharp method");
			stderr.WriteLine ("  f, frame         Get current stack frame");
			stderr.WriteLine ("  s, step          Single-step");
			stderr.WriteLine ("  n, next          Single-step");
			stderr.WriteLine ("  !, gdb           Send command to gdb");
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string line)
		{
			if (line == "") {
				if (last_command == null)
					return true;

				try {
					return ProcessCommand (last_command, last_args);
				} catch (TargetException e) {
					Console.WriteLine (e);
					stderr.WriteLine (e);
					return true;
				}
			}

			string[] tmp_args = line.Split (' ', '\t');
			string[] args = new string [tmp_args.Length - 1];
			Array.Copy (tmp_args, 1, args, 0, tmp_args.Length - 1);
			string command = tmp_args [0];

			last_command = null;
			last_args = new string [0];

			try {
				return ProcessCommand (tmp_args [0], args);
			} catch (TargetException e) {
				Console.WriteLine (e);
				stderr.WriteLine (e);
				return true;
			}
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string command, string[] args)
		{
			switch (command) {
			case "h":
			case "help":
				ShowHelp ();
				break;

			case "q":
			case "quit":
			case "exit":
				return false;

			case "r":
			case "run":
				backend.Run ();
				break;

			case "c":
			case "continue":
				if (backend.Inferior == null)
					throw new NoTargetException ();
				backend.Inferior.Continue ();
				last_command = command;
				break;

			case "i":
			case "stepi":
				backend.StepInstruction ();
				last_command = command;
				break;

			case "t":
			case "nexti":
				backend.NextInstruction ();
				last_command = command;
				break;

			case "s":
			case "step":
				backend.StepLine ();
				last_command = command;
				break;

			case "n":
			case "next":
				backend.NextLine ();
				last_command = command;
				break;

			case "abort":
				if (backend.Inferior == null)
					throw new NoTargetException ();
				backend.Inferior.Shutdown ();
				break;

			case "kill":
				if (backend.Inferior == null)
					throw new NoTargetException ();
				backend.Inferior.Kill ();
				break;

			case "sleep":
				Thread.Sleep (50000);
				break;

			case "b":
			case "break-method": {
				if (args.Length != 2) {
					stderr.WriteLine ("Command requires an argument");
					break;
				}

				ILanguageCSharp csharp = backend as ILanguageCSharp;
				if (csharp == null) {
					stderr.WriteLine ("Debugger doesn't support C#");
					break;
				}

				Type type = csharp.CurrentAssembly.GetType (args [0]);
				if (type == null) {
					stderr.WriteLine ("No such type: `" + args [0] + "'");
					break;
				}

				MethodInfo method = type.GetMethod (args [1]);
				if (method == null) {
					stderr.WriteLine ("Can't find method `" + args [1] + "' in type `" +
							  args [0] + "'");
					break;
				}

				ITargetLocation location = csharp.CreateLocation (method);
				if (location == null) {
					stderr.WriteLine ("Can't get location for method: " +
							  args [0] + "." + args [1]);
					break;
				}

				IBreakPoint break_point = backend.AddBreakPoint (location);

				if (break_point != null)
					stderr.WriteLine ("Added breakpoint: " + break_point);
				else
					stderr.WriteLine ("Unable to add breakpoint!");

				break;
			}

			default:
				stderr.WriteLine ("Unknown command: " + command);
				break;
			}

			return true;
		}
	}
}
