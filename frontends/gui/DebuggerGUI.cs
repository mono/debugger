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

		DebuggerBackend backend;
		Interpreter interpreter;

		SourceManager source_manager;

		public DebuggerGUI (string[] arguments)
		{
			program = new Program ("Debugger", "0.1", Modules.UI, arguments);

			backend = new DebuggerBackend ();

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
				gxml, null, (Gtk.Notebook) gxml ["register-notebook"]);
			backtrace_view = new BackTraceView (
				null, (Gtk.Container) gxml ["backtrace-view"]);

			current_insn = new CurrentInstructionEntry ((Gtk.Entry) gxml ["current-insn"]);

			//source_status = new SourceStatusbar ((Gtk.Statusbar) gxml ["source-status"]);
			source_view = new SourceView
			((Gtk.Container) main_window, (Gtk.TextView) gxml ["source-view"]);

			source_manager = new SourceManager ((Gtk.Notebook) gxml ["code-browser-notebook"]);

			disassembler_view = new DisassemblerView (
				null, (Gtk.TextView) gxml ["disassembler-view"]);
			
			gxml.Autoconnect (this);

			interpreter = new Interpreter (backend, output_writer, output_writer);

			command_entry.ActivatesDefault = true;
			command_entry.Activated += new EventHandler (DoOneCommand);
			command_entry.Sensitive = false;

			//
			// The items that we sensitize
			//
			StateRegister (
				new string [] {
					"run-button", "run-program-menu"},
				TargetState.EXITED, TargetState.NO_TARGET);

			//
			// I considered adding "register-notebook", but it looks
			// ugly.
			
			StateRegister (
				new string [] {
					"run-button", "run-program-menu",
					"step-over-button", "step-into-button",
					"step-into-menu", "step-over-menu",
					"instruction-step-into-menu",
					"instruction-step-over-menu" },
				TargetState.STOPPED);

			
			StateRegister (
				new string [] {
					"stop-button", "stop-program-menu"
				}, TargetState.RUNNING);

			StateSensitivityUpdate (TargetState.NO_TARGET);

			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);
			backend.DebuggerOutput += new TargetOutputHandler (DebuggerOutput);
			backend.DebuggerError += new DebuggerErrorHandler (DebuggerError);
		}

		ArrayList all_state_widgets = new ArrayList ();
		ArrayList [] state_widgets_map = new ArrayList [(int)TargetState.LAST];
		
		void StateRegister (string [] widget_names, params TargetState [] states)
		{
			foreach (string wname in widget_names){
				Widget w = gxml [wname];

				foreach (TargetState s in states){
					if (state_widgets_map [(int)s] == null)
						state_widgets_map [(int)s] = new ArrayList ();
					
					state_widgets_map [(int)s].Add (w);
				}
				if (!all_state_widgets.Contains (w))
					all_state_widgets.Add (w);
			}
		}
		
		void StateSensitivityUpdate (TargetState state)
		{
			foreach (Widget w in all_state_widgets)
				w.Sensitive = state_widgets_map [(int)state].Contains (w);
		}
			
		//
		// This constructor is used by the startup code: it contains the command line arguments
		//
		void LoadProgram (string [] args)
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

		//
		// This constructor takes the name of the program, and the arguments as a vector
		// for the program.
		//
		void LoadProgram (string program, string [] args)
		{
			backend.CommandLineArguments = args;
			backend.TargetApplication = program;

			main_window.Title = "Debugging: " + program +
				(args.Length > 0 ? (" " + String.Join (" ", args)) : "");

			target_status.SetBackend (backend);
			line_debug_status.SetBackend (backend);
			register_display.SetBackend (backend);
			backtrace_view.SetBackend (backend);
			current_insn.SetBackend (backend);
			disassembler_view.SetBackend (backend);
			source_view.SetBackend (backend);
			source_manager.SetBackend (backend);
			
			backend.StateChanged += new StateChangedHandler (BackendStateChanged);
		}

		void UpdateGUIState (TargetState state)
		{
			StateSensitivityUpdate (state);
		}
		
		//
		// Callbacks from the backend
		//
		void BackendStateChanged (TargetState state, int arg)
		{
			UpdateGUIState (state);
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
			if (!backend.HasTarget)
				backend.Run ();
			else
				backend.Continue ();
		}

		void OnStopProgramActivate (object sender, EventArgs args)
		{
			backend.Stop ();
		}

		void OnRestartProgramActivate (object sender, EventArgs args)
		{
			backend.Stop ();
			backend.Run ();
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

		void DebuggerOutput (string output)
		{
			AddOutput (output);
		}

		void DebuggerError (object sender, string message, Exception e)
		{
			AddOutput (String.Format ("Debugger error: {0}\n{1}", message, e));
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
