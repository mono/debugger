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
	public delegate void ProgramLoadedHandler (object sender, DebuggerBackend backend);
	public delegate void ProcessCreatedHandler (object sender, Process process);

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

			LogFunc func = new LogFunc (Log.PrintTraceLogFunction);
			Log.SetLogHandler ("Gtk", LogLevelFlags.All, func);
			Log.SetLogHandler ("GLib", LogLevelFlags.All, func);
			Log.SetLogHandler ("GLib-GObject", LogLevelFlags.All, func);
			Log.SetLogHandler ("GtkSourceView", LogLevelFlags.All, func);

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
		VariableDisplay variable_display;
		BackTraceView backtrace_view;
		ModuleDisplay module_display;
		ProcessManager process_manager;
		HexEditor hex_editor;
		MemoryMapsDisplay memory_maps_display;
		BreakpointManager breakpoint_manager;
		ThreadNotify thread_notify;

		Gtk.TextView target_output;
		Gtk.TextView command_output;

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

#if FALSE
			backend.DebuggerError += new DebuggerErrorHandler (ErrorHandler);
#endif

			SetupGUI ();

			main_window.DeleteEvent += new DeleteEventHandler (Window_Delete);

			context = new ScriptingContext (command_writer, output_writer, false, true);
			interpreter = new Interpreter (context);

			if (arguments.Length > 0)
				LoadProgram (arguments);
		}

		internal ThreadNotify ThreadNotify {
			get { return thread_notify; }
		}

		internal Glade.XML GXML {
			get { return gxml; }
		}

		public event ProgramLoadedHandler ProgramLoadedEvent;
		public event ProcessCreatedHandler ProcessCreatedEvent;

		protected void OnProgramLoadedEvent (DebuggerBackend backend)
		{
			if (ProgramLoadedEvent != null)
				ProgramLoadedEvent (this, backend);
		}

		protected void OnProcessCreatedEvent (Process process)
		{
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
		}

		//
		// Called back when the debugger finds an error
		//
		void ErrorHandler (object sender, string message, Exception e)
		{
			Report.Error (message);
		}

		//
		// Sets an image in a Button created by Glade.  Notice that glade will
		// insert an empty box inside it.
		//
		void SetButtonImage (string name)
		{
			Button b = (Button) gxml [name + "-button"];

			Pixbuf image = new Pixbuf (null, name + ".png");

			foreach (Widget w in b.Children){
				b.Remove (w);
			}
			
			b.Child = new Gtk.Image (image);
			b.ShowAll ();
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

			variable_display = new VariableDisplay (this, "variable-display");
			backtrace_view = new BackTraceView (this, "backtrace-view");
			module_display = new ModuleDisplay (this, "module-view");
			process_manager = new ProcessManager (this, "process-manager");
			hex_editor = new HexEditor (this, "hexeditor-dialog", "hexeditor-view");
			memory_maps_display = new MemoryMapsDisplay (this, "memory-maps-view");
			breakpoint_manager = new BreakpointManager (this, "breakpoint-manager");

			current_insn = new CurrentInstructionEntry (this, "current-insn");

			source_manager = new SourceManager (this, "code-browser-notebook",
							    "disassembler-view", "status-bar",
							    "register-notebook");

			SetButtonImage ("step-over");
			SetButtonImage ("step-into");
			
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

			backend = context.ParseArguments (args);
			start = context.ProcessStart;
			OnProgramLoadedEvent (backend);

			backend.ThreadManager.MainThreadCreatedEvent += new ThreadEventHandler (
				main_process_started);

			context.Run ();

			//
			// FIXME: chdir here to working_dir
			//

			main_window.Title = "Debugging: " + program +
				(args.Length > 0 ? (" " + String.Join (" ", args)) : "");
		}

		void main_process_started (ThreadManager manager, Process process)
		{
			this.process = process;

			process.TargetOutput += new TargetOutputHandler (TargetOutput);
			process.TargetError += new TargetOutputHandler (TargetError);
			process.DebuggerOutput += new TargetOutputHandler (DebuggerOutput);
			process.DebuggerError += new DebuggerErrorHandler (DebuggerError);

			OnProcessCreatedEvent (process);

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
		void OnFileOpenActivate (object sender, EventArgs args)
		{
			Gtk.FileSelection fs = new Gtk.FileSelection ("Open File");
			fs.ShowFileops = false;

			int v = fs.Run ();
			string file = fs.Filename;
			fs.Destroy ();
			fs = null;

			if (v != -5)
				return;

			source_manager.LoadFile (file);
		}

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
			hex_editor.RunDialog ();
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
				if (backend != null) {
					backend.Quit ();
					backend.Dispose ();
					backend = null;
				}
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
