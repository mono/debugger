using System;
using System.Text;
using System.Reflection;
using System.Threading;
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
		GDB backend;

		Interpreter (string application, string[] arguments)
		{
			backend = new GDB (application, arguments);

			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);
			backend.StateChanged += new StateChangedHandler (StateChanged);

			backend.SourceFileFactory = new SourceFileFactory ();
		}

		void ShowHelp ()
		{
			Console.WriteLine ("Commands:");
			Console.WriteLine ("  q, quit,exit     Quit the debugger");
			Console.WriteLine ("  r, run           Start the target");
			Console.WriteLine ("  c, continue      Continue the target");
			Console.WriteLine ("  abort            Abort the target");
			Console.WriteLine ("  kill             Kill the target");
			Console.WriteLine ("  b, break-method  Add breakpoint for a CSharp method");
			Console.WriteLine ("  !, gdb           Send command to gdb");
		}

		StringBuilder output_builder = null;

		bool DoOneCommand (string command, string[] args)
		{
			switch (command) {
			case "h":
			case "help":
				ShowHelp ();
				break;

			case "q":
			case "quit":
			case "exit":
				backend.Quit ();
				return false;

			case "r":
			case "run":
				backend.Run ();
				break;

			case "c":
			case "continue":
				backend.Continue ();
				break;

			case "abort":
				backend.Abort ();
				break;

			case "kill":
				backend.Kill ();
				break;

			case "sleep":
				Thread.Sleep (50000);
				break;

			case "b":
			case "break-method": {
				if (args.Length != 2) {
					Console.WriteLine ("Command requires an argument");
					break;
				}

				ILanguageCSharp csharp = backend as ILanguageCSharp;
				if (csharp == null) {
					Console.WriteLine ("Debugger doesn't support C#");
					break;
				}

				Type type = csharp.CurrentAssembly.GetType (args [0]);
				if (type == null) {
					Console.WriteLine ("No such type: `{0}'", args [0]);
					break;
				}

				MethodInfo method = type.GetMethod (args [1]);
				if (method == null) {
					Console.WriteLine ("Can't find method `{0}' in type `{1}'",
							   args [1], args [0]);
					break;
				}

				ITargetLocation location = csharp.CreateLocation (method);
				if (location == null) {
					Console.WriteLine ("Can't get location for method: {0}.{1}",
							   args [0], args [1]);
					break;
				}

				IBreakPoint break_point = backend.AddBreakPoint (location);

				if (break_point != null)
					Console.WriteLine ("Added breakpoint: " + break_point);
				else
					Console.WriteLine ("Unable to add breakpoint!");

				break;
			}

			case "!":
			case "gdb": {
				GDB gdb = backend as GDB;
				if (gdb == null) {
					Console.WriteLine ("This command is only available when using " +
							   "gdb as backend");
					break;
				}

				output_builder = new StringBuilder ();
				gdb.SendUserCommand (String.Join (" ", args));
				Console.WriteLine (output_builder.ToString ());
				output_builder = null;
				break;
			}

			default:
				Console.WriteLine ("Unknown command: " + command);
				break;
			}

			return true;
		}

		void TargetOutput (string output)
		{
			if (output_builder != null)
				output_builder.Append (output);
			else
				Console.WriteLine ("OUTPUT: |" + output + "|");
		}

		void TargetError (string output)
		{
			Console.WriteLine ("ERROR : |" + output + "|");
		}

		void StateChanged (TargetState new_state)
		{
			Console.WriteLine ("STATE CHANGED: " + new_state);
		}

		public void MainLoop ()
		{
			bool keep_running = true;

			while (keep_running) {
				Console.Write ("$ ");

				string line = Console.ReadLine ();
				if (line == null)
					break;

				if (line == "")
					continue;

				string[] args = line.Split (' ', '\t');
				string[] new_args = new string [args.Length - 1];
				Array.Copy (args, 1, new_args, 0, args.Length - 1);

				keep_running = DoOneCommand (args [0], new_args);
			}

			backend.Dispose ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
					backend.Dispose ();
				}
				
				// Release unmanaged resources
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Interpreter ()
		{
			Dispose (false);
		}

		//
		// Main
		//
		static void Main (string[] args)
		{
			if (args.Length < 1) {
				Console.WriteLine ("Usage: {0} application.exe [args]",
						   AppDomain.CurrentDomain.FriendlyName);
				Environment.Exit (1);
			}

			string[] new_args = new string [args.Length - 1];
			Array.Copy (args, 1, new_args, 0, args.Length - 1);

			Interpreter interpreter = new Interpreter (args [0], new_args);

			interpreter.MainLoop ();
			interpreter.Dispose ();
		}
	}
}
