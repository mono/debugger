using System;
using GLib;
using Gtk;
using GtkSharp;
using Gnome;
using Glade;

namespace Mono.Debugger.GUI
{
	public delegate void ProgramToDebugHandler (string program, string working_dir, string arguments);

	public class ProgramToDebug
	{
		FileEntry program;
		Gtk.Entry working_dir, arguments;

		Dialog d;
	
		public ProgramToDebug (Glade.XML gxml, string name, string [] args)
		{
			d = (Dialog) gxml ["program-open-dialog"];
		
			program = (FileEntry) gxml ["program-to-debug-entry"];
			working_dir = (Gtk.Entry) gxml ["working-directory-entry"];
			arguments = (Gtk.Entry) gxml ["arguments-entry"];

			d.Response += new ResponseHandler (response_event);
		}

		public event ProgramToDebugHandler ActivatedEvent;

		void response_event (object sender, ResponseArgs args)
		{
			d.Hide ();
			if (args.ResponseId != 0)
				return;

			if (ActivatedEvent != null)
				ActivatedEvent (program.Filename, working_dir.Text, arguments.Text);
		}

		public void ShowDialog ()
		{
			d.Show ();
		}
	}
}
