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
			bool gotarg = false;
			ArrayList arguments = new ArrayList ();

			foreach (string a in args){
				if (gotarg == false && a.StartsWith ("--")){
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
				} else {
					gotarg = true;
					arguments.Add (a);
				}
			}

			DebuggerGUI gui = new DebuggerGUI ((string []) arguments.ToArray (typeof (string)));

			try {
				gui.Run ();
			} catch (System.Reflection.TargetInvocationException e) {
				Console.WriteLine (e.InnerException.ToString ());
			}

			Environment.Exit (0);
		}

		Program program;
		ProcessStart start;
		Glade.XML gxml;
		App main_window;

		Gtk.Entry command_entry;
		CurrentInstructionEntry current_insn;
		RegisterDisplay register_display;
		VariableDisplay variable_display;
		BackTraceView backtrace_view;
		ModuleDisplay module_display;
		ProcessManager process_manager;
		HexEditor hex_editor;
		MemoryMapsDisplay memory_maps_display;
		BreakpointManager breakpoint_manager;
		Dialog hex_editor_dialog;
		ThreadNotify thread_notify;

		Gtk.TextView target_output;
		Gtk.TextView command_output;
		SourceStatusbar source_status;

		DebuggerTextWriter output_writer;
		DebuggerTextWriter command_writer;

		DebuggerBackend backend;
		Interpreter interpreter;
		ScriptingContext context;
		Process process;

		SourceManager source_manager;
		string working_dir = ".";

		void Window_Delete (object obj, DeleteEventArgs args)
		{
			program.Quit();
			args.RetVal = true;
		}

		public DebuggerGUI (string[] arguments)
		{
			thread_notify = new ThreadNotify ();
			program = new Program ("Debugger", "0.2", Modules.UI, arguments);

			backend = new DebuggerBackend ();
#if FALSE
			backend.DebuggerError += new DebuggerErrorHandler (ErrorHandler);
#endif

			SetupGUI ();

			main_window.DeleteEvent += new DeleteEventHandler (Window_Delete);

			interpreter = new Interpreter (command_writer, output_writer);
			context = interpreter.Context;

			backend.ThreadManager.InitializedEvent += new ThreadEventHandler (main_process_started);

			if (arguments.Length > 0)
				LoadProgram (arguments);
		}

		internal ThreadNotify ThreadNotify {
			get { return thread_notify; }
		}

		internal Glade.XML GXML {
			get { return gxml; }
		}

		internal DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		//
		// Called back when the debugger finds an error
		//
		void ErrorHandler (object sender, string message, Exception e)
		{
			Report.Error (message);
		}
		
		//
		// Does the initial GUI Setup
		//
		void SetupGUI ()
		{
			GtkSharp.Mono.Debugger.GUI.ObjectManager.Initialize ();

			gxml = new Glade.XML (null, "debugger.glade", null, null);

			main_window = (App) gxml ["debugger-toplevel"];

			command_entry = (Gtk.Entry) gxml ["command-entry"];
			command_output = (Gtk.TextView) gxml ["command-output"];
			target_output = (Gtk.TextView) gxml ["target-output"];

			output_writer = new OutputWindow (target_output);
			command_writer = new OutputWindow (command_output);

			register_display = new RegisterDisplay (
				this, null, (Gtk.Notebook) gxml ["register-notebook"]);
			variable_display = new VariableDisplay (
				this, null, (Gtk.Container) gxml ["variable-display"]);
			backtrace_view = new BackTraceView (
				this, null, (Gtk.Container) gxml ["backtrace-view"]);
			module_display = new ModuleDisplay (
				this, null, (Gtk.Container) gxml ["module-view"]);
			process_manager = new ProcessManager (
				this, null, (Gtk.Container) gxml ["process-manager"]);
			hex_editor_dialog = (Gtk.Dialog) gxml ["hexeditor-dialog"];
			hex_editor = new HexEditor (
				this, hex_editor_dialog, (Gtk.Container) gxml ["hexeditor-view"]);
			memory_maps_display = new MemoryMapsDisplay (
				this, null, (Gtk.Container) gxml ["memory-maps-view"]);
			breakpoint_manager = new BreakpointManager (
				this, null, (Gtk.Container) gxml ["breakpoint-manager"]);

			current_insn = new CurrentInstructionEntry (
				this, (Gtk.Container) gxml ["current-insn"]);

			source_status = new SourceStatusbar (this, (Gtk.Statusbar) gxml ["status-bar"]);
			source_manager = new SourceManager (this, (Gtk.Notebook) gxml ["code-browser-notebook"],
							    (Gtk.Container) gxml ["disassembler-view"],
							    source_status, register_display);

			gxml.Autoconnect (this);

			command_entry.ActivatesDefault = true;
			command_entry.Activated += new EventHandler (DoOneCommand);
			command_entry.Sensitive = false;

			//
			// The items that we sensitize
			//
			StateRegister (
				new string [] {
					"run-button", "run-program-menu",
					"program-to-debug-menu"
				},
				TargetState.NO_TARGET);

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

#if FALSE
			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);
			backend.DebuggerOutput += new TargetOutputHandler (DebuggerOutput);
			backend.DebuggerError += new DebuggerErrorHandler (DebuggerError);
#endif
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
			foreach (Widget w in all_state_widgets) {
				ArrayList map = (ArrayList) state_widgets_map [(int)state];
				if (map == null)
					w.Sensitive = false;
				else
					w.Sensitive = map.Contains (w);
			}
		}
			
		//
		// This constructor is used by the startup code: it contains the command line arguments
		//
		void LoadProgram (string [] args)
		{
			IDictionary envars;

			envars = Environment.GetEnvironmentVariables();
			string [] env = new string [envars.Count];
			int i = 0;

			foreach (string name in envars.Keys) {
				env[i] = name + "=" + Environment.GetEnvironmentVariable (name);
				//Console.WriteLine ("env: " + env[i]);
				i++;
			}

			process = context.Start (args);
			start = context.ProcessStart;

			//
			// FIXME: chdir here to working_dir
			//

			main_window.Title = "Debugging: " + program +
				(args.Length > 0 ? (" " + String.Join (" ", args)) : "");

#if FALSE
			variable_display.SetProcess (process);
			hex_editor.SetProcess (process);
			breakpoint_manager.SetProcess (process);
#endif
		}

		void main_process_started (ThreadManager manager, Process process)
		{
			process.TargetOutput += new TargetOutputHandler (TargetOutput);
			process.TargetError += new TargetOutputHandler (TargetError);
			process.DebuggerOutput += new TargetOutputHandler (DebuggerOutput);
			process.DebuggerError += new DebuggerErrorHandler (DebuggerError);

			current_insn.SetProcess (process);
			register_display.SetProcess (process);
			source_status.SetProcess (process);
			backtrace_view.SetProcess (process);
			source_manager.SetProcess (process);
			module_display.SetProcess (process);
			memory_maps_display.SetProcess (process);
			process_manager.SetProcess (process);

			source_manager.StateChangedEvent += new StateChangedHandler (UpdateGUIState);

			StateSensitivityUpdate (TargetState.STOPPED);
		}

		void UpdateGUIState (TargetState state, int arg)
		{
			StateSensitivityUpdate (state);
		}
		
		//
		// Callbacks hooked from the Glade file.
		//
		ProgramToDebug program_to_debug;
		void OnProgramToDebugActivate (object sender, EventArgs a)
		{
			string program, arg_string;
			
			if (program_to_debug == null)
				program_to_debug = new ProgramToDebug (gxml, "", null);

			if (!program_to_debug.RunDialog (out program, out arg_string, ref working_dir))
				return;

			ArrayList list = new ArrayList ();
			list.Add (program);
			list.AddRange (arg_string.Split (new char [] { ' ' }));

			string[] argsv = new string [list.Count];
			list.CopyTo (argsv);

			LoadProgram (argsv);
		}

		void OnQuitActivate (object sender, EventArgs args)
		{
			if (backend != null) {
				backend.Quit ();
				backend.Dispose ();
			}

			program.Quit ();
		}

		void OnCPUViewActivate (object sender, EventArgs args)
		{
			Gtk.Notebook n;
			
			n = (Gtk.Notebook) gxml ["code-browser-notebook"];
			n.Page = 0;
		}

		void OnRunProgramActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.HasTarget)
				process.Continue (false);
		}

		void OnContinueIgnoreSignalActivate (object sender, EventArgs args)
		{
			if (process != null)
				process.ClearSignal ();
			OnRunProgramActivate (sender, args);
		}

		void OnStopProgramActivate (object sender, EventArgs args)
		{
			if (process != null)
				process.Stop ();
		}

		void OnStepIntoActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.CanStep)
				process.StepLine (false);
		}

		void OnStepOverActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.CanStep)
				process.NextLine (false);
		}

		void OnStepOutActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.CanStep)
				process.Finish (false);
		}

		void OnInstructionStepIntoActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.CanStep)
				process.StepInstruction (false);
		}

		void OnInstructionStepOverActivate (object sender, EventArgs args)
		{
			if ((process != null) && process.CanStep)
				process.NextInstruction (false);
		}
		
		void OnAboutActivate (object sender, EventArgs args)
		{
			Pixbuf pixbuf = new Pixbuf (null, "mono.png");

			About about = new About ("Mono Debugger", "0.1",
						 "Copyright (C) 2002 Ximian, Inc.",
						 "",
						 new string [] { "Martin Baulig (martin@ximian.com)",
								 "Miguel de Icaza (miguel@ximian.com)" },
						 new string [] { },
						 "", pixbuf);
			about.Run ();
		}

		void OnViewHexEditor (object sender, EventArgs args)
		{
			hex_editor_dialog.Show ();
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
			// output_writer.WriteLine (output);
		}

		void DoOneCommand (object sender, EventArgs event_args)
		{
			string line = command_entry.Text;
			command_entry.Text = "";

			if (interpreter == null)
				return;

			if (!interpreter.ProcessCommand (line)) {
				backend.Quit ();
				backend.Dispose ();
				Application.Quit ();
			}
		}

		public void Run ()
		{
			command_writer.WriteLine (false, "Debugger ready.");
			command_entry.Sensitive = true;
			command_entry.HasFocus = true;
			program.Run ();
		}
	}
}
