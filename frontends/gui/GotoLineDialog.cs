using System;
using System.Collections;
using GLib;
using Gtk;
using GtkSharp;
using Gnome;
using Glade;

namespace Mono.Debugger.GUI
{
	public class GotoLineDialog
	{
		Dialog d;
		SourceManager manager;
		Gtk.Entry entry;
	
		public GotoLineDialog (Glade.XML gxml, SourceManager manager)
		{
			this.manager = manager;

			d = (Dialog) gxml ["goto-line-dialog"];
		
			entry = (Gtk.Entry) gxml ["goto-line-entry"];
			entry.Activated += new EventHandler (activated_event);

			d.Response += new ResponseHandler (response_event);
		}

		void activated_event (object o, EventArgs args)
		{
			do_response ();
		}

		bool do_response ()
		{
			try {
				manager.GotoLine ((int) UInt32.Parse (entry.Text));
				return true;
			} catch {
				return false;
			}
		}

		void response_event (object sender, ResponseArgs args)
		{
			if (args.ResponseId != (int) ResponseType.Ok) {
				d.Hide ();
				entry.Text = "";
				return;
			}

			if (do_response ())
				d.Hide ();
		}

		public void ShowDialog ()
		{
			d.Show ();
		}
	}
}
