using GLib;
using Gtk;
using Gdk;
using GtkSharp;
using Gnome;
using Glade;
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Reflection;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontends.CommandLine;
using Mono.Debugger.GUI;

namespace Mono.Debugger.GUI
{
	public class DebuggerGUI
	{
		static void Usage ()
		{
			Console.WriteLine (
				"Mono debugger, (C) 2002 Ximian, Inc.\n\n" +
				"To debug a C# application:\n" +
				"  {0} [options] application.exe [args]\n\n" +
				"To debug a native application:\n" +
				"  {0} [options] native application.exe [args]\n\n" +
				"Native applications can only be debugged if they have\n" +
				"been compiled with GCC 3.1 and -gdarf-2.\n\n",
				AppDomain.CurrentDomain.FriendlyName);
			Console.WriteLine (
				"Options:\n" +
				"  --help               Show this help text.\n" +
				"\n");
			Environment.Exit (1);
		}

		//
		// Main
		//
		static void Main (string[] args)
		{
			ArrayList arguments = new ArrayList ();

			foreach (string a in args){
				if (a.StartsWith ("--")){
					string name = a.Substring (2);
					
					switch (name) {
					case "help":
						Usage ();
						break;
						
					default:
						Console.WriteLine ("Unknown argument `{0}'.", name);
						Environment.Exit (1);
						break;
					}
				} else
					arguments.Add (a);
			}

			DebuggerGUI gui = new DebuggerGUI ((string []) arguments.ToArray (typeof (string)));

			gui.Run ();
		}

		Program program;
		Glade.XML gxml;
		App main_window;

		Gtk.Entry command_entry;
		CurrentInstructionEntry current_insn;
		SourceView source_view;
		DisassemblerView disassembler_view;
		RegisterDisplay register_display;
		BackTraceView backtrace_view;

		Gtk.TextView target_output;
		TargetStatusbar target_status;
		SourceStatusbar source_status;
		LineDebugStatusbar line_debug_status;

		TextWriter output_writer;

		IDebuggerBackend backend;
		Interpreter interpreter;

		public DebuggerGUI (string[] arguments)
		{
			program = new Program ("Debugger", "0.1", Modules.UI, arguments);

			SetupGUI ();
			
			if (arguments.Length > 0)
				LoadProgram (arguments);
		}

		//
		// Does the initial GUI Setup
		//
		void SetupGUI ()
		{
			gxml = new Glade.XML (null, "debugger.glade", null, null);

			main_window = (App) gxml ["debugger-toplevel"];

			command_entry = (Gtk.Entry) gxml ["command-entry"];
			target_output = (Gtk.TextView) gxml ["target-output"];

			output_writer = new OutputWindow (target_output);

			target_status = new TargetStatusbar ((Gtk.Statusbar) gxml ["target-status"]);
			line_debug_status = new LineDebugStatusbar ((Gtk.Statusbar) gxml ["line-debug-status"]);
			register_display = new RegisterDisplay (
				null, (Gtk.Container) gxml ["register-view"]);
			backtrace_view = new BackTraceView (
				null, (Gtk.Container) gxml ["backtrace-view"]);

			current_insn = new CurrentInstructionEntry ((Gtk.Entry) gxml ["current-insn"]);

			source_status = new SourceStatusbar ((Gtk.Statusbar) gxml ["source-status"]);
			source_view = new SourceView
				((Gtk.Container) main_window, (Gtk.TextView) gxml ["source-view"]);
			disassembler_view = new DisassemblerView (
				null, (Gtk.TextView) gxml ["disassembler-view"]);
			
			gxml.Autoconnect (this);

			command_entry.ActivatesDefault = true;
			command_entry.Activated += new EventHandler (DoOneCommand);
			command_entry.Sensitive = false;
		}

		bool ProgramIsManaged (string name)
		{
			//
			// Should look for the file header
			//
			return (name.IndexOf (".exe") >= 0);
		}

		//
		// This constructor is used by the startup code: it contains the command line arguments
		//
		void LoadProgram (string [] args)
		{
			string [] program_args = new string [args.Length-1];
			if (args.Length > 1)
				Array.Copy (args, 1, program_args, 0, args.Length-1);

			LoadProgram (args [0], program_args);
		}

		//
		// This constructor takes the name of the program, and the arguments as a vector
		// for the program.
		//
		void LoadProgram (string program, string [] args)
		{
			if (ProgramIsManaged (program))
				backend = new Mono.Debugger.Backends.Debugger (program, args);
			else {
				string [] unmanaged_args = new string [args.Length+1];
				args.CopyTo (unmanaged_args, 1);
				unmanaged_args [0] = program;
				
				backend = new Mono.Debugger.Backends.Debugger (unmanaged_args);
			}

			interpreter = new Interpreter (backend, output_writer, output_writer);
			
			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);

			main_window.Title = "Debugging: " + program +
				(args.Length > 0 ? (" " + String.Join (" ", args)) : "");

			target_status.SetBackend (backend);
			line_debug_status.SetBackend (backend);
			register_display.SetBackend (backend);
			backtrace_view.SetBackend (backend);
			current_insn.SetBackend (backend);
			disassembler_view.SetBackend (backend);
			source_view.SetBackend (backend);
		}
		
		//
		// Callbacks hooked from the Glade file.
		//
		ProgramToDebug program_to_debug;
		void OnProgramToDebugActivate (object sender, EventArgs a)
		{
			string program = null, arg_string = null, working_dir = null;
			
			if (program_to_debug == null)
				program_to_debug = new ProgramToDebug (gxml, "", null);

			if (!program_to_debug.RunDialog (out program, out arg_string, out working_dir))
				return;
			
			string [] argsv = arg_string.Split (new char [] { ' ' });

			LoadProgram (program, argsv);
		}
		
		void OnQuitActivate (object sender, EventArgs args)
		{
			if (backend != null)
				backend.Quit ();
			
			Application.Quit ();
		}

		void OnCPUViewActivate (object sender, EventArgs args)
		{
			Gtk.Notebook n;
			
			n = (Gtk.Notebook) gxml ["code-browser-notebook"];
			n.Page = 0;
		}

		void OnRunProgramActivate (object sender, EventArgs args)
		{
			if (backend == null)
			if (backend.Inferior == null)
				backend.Run ();
			else {
				Console.WriteLine ("Do not know how to continue");

				//
				// Maybe this works?
				//
				while (backend.State != TargetState.NO_TARGET)
					backend.Finish ();
			}
		}

		void OnStopProgramActivate (object sender, EventArgs args)
		{
			Console.WriteLine ("Do not know how to stop program");
		}

		void OnRestartProgramActivate (object sender, EventArgs args)
		{
			Console.WriteLine ("Do not know how to stop program");	
		}

		void OnStepIntoActivate (object sender, EventArgs args)
		{
			backend.StepLine ();
		}

		void OnStepOverActivate (object sender, EventArgs args)
		{
			backend.NextLine ();
		}

		void OnStepOutActivate (object sender, EventArgs args)
		{
			backend.Finish ();
		}

		void OnInstructionStepIntoActivate (object sender, EventArgs args)
		{
			backend.StepInstruction ();
		}

		void OnInstructionStepOverActivate (object sender, EventArgs args)
		{
			backend.NextInstruction ();
		}
		
		void OnAboutActivate (object sender, EventArgs args)
		{
			Pixbuf pixbuf = new Pixbuf (null, "mono.png");

			About about = new About ("Mono Debugger", "0.1",
						 "Copyright (C) 2002 Ximian, Inc.",
						 "",
						 new string [] { "Martin Baulig (martin@gnome.org)", "Miguel de Icaza (miguel@ximian.com)" },
						 new string [] { },
						 "", pixbuf);
			about.Run ();
		}

		void TargetOutput (string output)
		{
			AddOutput (output);
		}

		void TargetError (string output)
		{
			AddOutput (output);
		}

		void AddOutput (string output)
		{
			Console.WriteLine (output);
			output_writer.WriteLine (output);
		}

		void DoOneCommand (object sender, EventArgs event_args)
		{
			string line = command_entry.Text;
			command_entry.Text = "";

			if (!interpreter.ProcessCommand (line)) {
				backend.Quit ();
				Application.Quit ();
			}
		}

		public void Run ()
		{
			output_writer.WriteLine ("Debugger ready.");
			output_writer.WriteLine ("Type a command in the command entry or `h' for help.");
			command_entry.Sensitive = true;
			command_entry.HasFocus = true;
			program.Run ();
		}
	}
}
