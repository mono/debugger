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

			DebuggerGUI gui = new DebuggerGUI (args [0], new_args);

			gui.Run ();
		}

		Program program;
		Glade.XML gxml;

		Gtk.Entry command_entry;
		CurrentInstructionEntry current_insn;
		SourceView source_view;

		Gtk.TextView target_output;
		TargetStatusbar target_status;
		SourceStatusbar source_status;

		TextWriter output_writer;

		IDebuggerBackend backend;
		Interpreter interpreter;

		public DebuggerGUI (string application, string[] arguments)
		{
			program = new Program ("Debugger", "0.1", Modules.UI, arguments);

			NameValueCollection settings = ConfigurationSettings.AppSettings;

			string fname = "frontends/gui/mono-debugger.glade";
			string root = "main_window";
			string target = "target_window";

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "gui-glade-file":
					fname = value;
					break;

				case "gui-glade-root":
					root = value;
					break;

				case "gui-glade-target":
					target = value;
					break;

				default:
					break;
				}
			}

			gxml = new Glade.XML (fname, null, null);

			Widget target_widget = gxml [target];
			target_widget.Show ();

			Widget main_widget = gxml [root];
			main_widget.Show ();

			command_entry = (Gtk.Entry) gxml ["command_entry"];
			target_output = (Gtk.TextView) gxml ["target_output"];

			output_writer = new OutputWindow (target_output);

			backend = new Debugger (application, arguments);

			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);

			target_status = new TargetStatusbar (backend, (Gtk.Statusbar) gxml ["target_status"]);
			source_status = new SourceStatusbar (backend, (Gtk.Statusbar) gxml ["source_status"]);
			source_view = new SourceView (backend, (Gtk.TextView) gxml ["source_view"]);
			current_insn = new CurrentInstructionEntry (backend, (Gtk.Entry) gxml ["current_insn"]);

			interpreter = new Interpreter (backend, output_writer, output_writer);

			command_entry.ActivatesDefault = true;
			command_entry.Activated += new EventHandler (DoOneCommand);
			command_entry.Sensitive = false;
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
