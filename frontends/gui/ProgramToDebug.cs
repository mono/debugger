using System;
using GLib;
using Gtk;
using Gnome;
using Glade;

class ProgramToDebug {
	FileEntry program;
	Gtk.Entry working_dir, arguments;

	Dialog d;
	
	public ProgramToDebug (Glade.XML gxml, string name, string [] args)
	{
		d = (Dialog) gxml ["program-open-dialog"];
		
		program = (FileEntry) gxml ["program-to-debug-entry"];
		working_dir = (Gtk.Entry) gxml ["working-directory-entry"];
		arguments = (Gtk.Entry) gxml ["arguments-entry"];
	}

	public bool RunDialog (out string res_program, out string res_args, out string res_working_dir)
	{
		res_program = null;
		res_args = null;
		res_working_dir = ".";

		int v = d.Run ();

		if (v == 0){
			res_program = program.Filename;
			res_args = arguments.Text;
			res_working_dir = working_dir.Text;
		}
		d.Hide ();

		return v == 0;
	}
}
