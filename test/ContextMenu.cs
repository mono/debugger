using System;
using System.Text;
using System.IO;
using GLib;
using Gtk;
using GtkSharp;

public class ContextMenu
{
	Gtk.Window win;
	Gtk.VBox vbox;
	Gtk.ScrolledWindow sw;
	Gtk.TextBuffer text_buffer;
	Gtk.TextView text_view;

	public void CreateWidgets ()
	{
		win = new Gtk.Window ("Test context menu");

		vbox = new Gtk.VBox (false, 0);
		win.Add (vbox);

		text_buffer = new Gtk.TextBuffer (new Gtk.TextTagTable ());
		text_view = new Gtk.TextView (text_buffer);
		text_view.PopulatePopup += new PopulatePopupHandler (populate_view);
		text_view.Editable = false;

		sw = new Gtk.ScrolledWindow ();
		sw.Add (text_view);

		StringBuilder sb = new StringBuilder ();
		for (int i = 1; i < 100; i++)
			sb.Append (i.ToString () + "\n");

		text_buffer.Text = sb.ToString ();

		vbox.PackStart (sw, true, true, 8);

		win.DefaultWidth = 800;
		win.DefaultHeight = 500;

		win.DeleteEvent += new DeleteEventHandler (delete_event);
		win.ShowAll ();
	}

	protected void Prepend (Gtk.Menu menu, string text, EventHandler cb)
	{
		Gtk.MenuItem item = new MenuItem (text);
		item.Show ();
		item.Activated += cb;
		menu.Prepend (item);
	}

	void InsertBreakpointAtXY (int x, int y)
	{
		int buffer_x, buffer_y;

		Console.WriteLine ("INSERT BREAKPOINT: {0},{1}", x, y);

		text_view.WindowToBufferCoords (
			TextWindowType.Widget, x, y, out buffer_x, out buffer_y);

		Gtk.TextIter iter;
		int line_top;
		text_view.GetLineAtY (out iter, buffer_y, out line_top);
		int line = iter.Line + 1;

		Console.WriteLine ("INSERT BREAKPOINT: {0},{1} - {2},{3} - {4} - {5}",
				   x, y, buffer_x, buffer_y, line_top, line);
	}

	void insert_breakpoint (object o, EventArgs a)
	{
		// FIXME: This is getting the wrong location !
		Gdk.EventButton bevent = (Gdk.EventButton) Gtk.Application.CurrentEvent;
		InsertBreakpointAtXY ((int) bevent.x, (int) bevent.y);
	}

	void populate_view (object o, PopulatePopupArgs args)
	{
		args.Menu.Prepend (new SeparatorMenuItem ());
		Prepend (args.Menu, "_Insert Breakpoint", new EventHandler (insert_breakpoint));
	}

	void delete_event (object obj, DeleteEventArgs args)
	{
		SignalArgs sa = (SignalArgs) args;
		Application.Quit ();
		sa.RetVal = true;
	}

	public static int Main ()
	{
		Application.Init ();
		ContextMenu test = new ContextMenu ();
		test.CreateWidgets ();
		Application.Run ();
		return 0;
	}
}
