using System;
using System.Collections;
using GLib;
using Gtk;
using GtkSharp;
using Gnome;
using Glade;

using Mono.Debugger;
using Mono.Debugger.Frontends.CommandLine;

namespace Mono.Debugger.GUI
{
	public delegate void ProgramToDebugHandler (ProcessStart start);

	public class ProgramToDebug
	{
		FileEntry program;
		ScriptingContext context;
		Gtk.Entry working_dir, arguments, optimizations;

		Dialog d;
	
		public ProgramToDebug (Glade.XML gxml, ScriptingContext context)
		{
			this.context = context;

			d = (Dialog) gxml ["program-open-dialog"];
		
			program = (FileEntry) gxml ["program-to-debug-entry"];
			working_dir = (Gtk.Entry) gxml ["working-directory-entry"];
			arguments = (Gtk.Entry) gxml ["arguments-entry"];
			optimizations = (Gtk.Entry) gxml ["jit-optimizations-entry"];

			d.Response += new ResponseHandler (response_event);
		}

		public event ProgramToDebugHandler ActivatedEvent;

		void response_event (object sender, ResponseArgs args)
		{
			d.Hide ();
			if (args.ResponseId != 0)
				return;

			if (ActivatedEvent == null)
				return;

			ArrayList list = new ArrayList ();
			list.Add (program.Filename);
			list.AddRange (arguments.Text.Split (new char [] { ' ' }));

			string[] argsv = new string [list.Count];
			list.CopyTo (argsv);

			string opt_flags = optimizations.Text;
			if (opt_flags == "")
				opt_flags = null;

			ProcessStart start = context.Start (argsv, opt_flags);
			ActivatedEvent (start);
		}

		public void ShowDialog ()
		{
			d.Show ();
		}
	}
}
