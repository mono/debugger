using System;
using System.IO;
using GLib;
using Gtk;
using GtkSharp;

public class TestSourceView
{
	Gtk.SourceBuffer text_buffer;
	Gtk.SourceView source_view;
	string filename;

	static Gtk.Notebook notebook;

	public static void CreateWidgets ()
	{
		Gtk.Window win = new Gtk.Window ("SourceView Test");

		Gtk.VBox vbox = new Gtk.VBox (false, 0);
		win.Add (vbox);

		notebook = new Gtk.Notebook ();
		vbox.PackStart (notebook, true, true, 8);

		Gtk.Entry entry = new Gtk.Entry ();
		entry.Activated += new EventHandler (activated_event);

		vbox.PackStart (entry, false, false, 8);

		new TestSourceView ("/home/martin/monocvs/debugger/NEWS");

		win.DefaultWidth = 800;
		win.DefaultHeight = 500;

		win.DeleteEvent += new DeleteEventHandler (delete_event);
		win.ShowAll ();
	}

	public TestSourceView (string filename)
	{
		text_buffer = new Gtk.SourceBuffer (new Gtk.TextTagTable ());
		source_view = new Gtk.SourceView (text_buffer);
		source_view.Editable = false;

		text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

		Gtk.ScrolledWindow sw = new Gtk.ScrolledWindow ();
		sw.Add (source_view);

		string name = Path.GetFileName (filename);

		this.filename = filename;
		sw.Mapped += new EventHandler (mapped_event);

		sw.ShowAll ();

		notebook.InsertPage (sw, new Gtk.Label (name), -1);
		notebook.NextPage ();
	}

	void mapped_event (object o, EventArgs args)
	{
		Console.WriteLine ("MAPPED!");
		LoadFile (filename);
	}

	void LoadFile (string filename)
	{
		string contents = FileUtils.GetFileContents (filename);
		string[] lines = contents.Split ('\n');

		Console.WriteLine ("Total lines: {0}", lines.Length);

		int line = lines.Length / 2;
		Console.WriteLine ("Going to line: {0}", line);

		text_buffer.Text = contents;

		Gtk.TextIter iter;
		text_buffer.GetIterAtLineOffset (out iter, line, 0);

		Gtk.TextMark frame_mark = text_buffer.GetMark ("frame");
		text_buffer.MoveMark (frame_mark, iter);
		source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
	}

	static void activated_event (object obj, EventArgs args)
	{
		Gtk.Entry entry = (Gtk.Entry) obj;

		try {
			new TestSourceView (entry.Text);
		} catch (Exception e) {
			Console.WriteLine ("Cannot load file: {0}", entry.Text, e.Message);
		}
	}

	static void delete_event (object obj, DeleteEventArgs args)
	{
		SignalArgs sa = (SignalArgs) args;
		Application.Quit ();
		sa.RetVal = true;
	}

	public static int Main ()
	{
		Application.Init ();
		CreateWidgets ();
		Application.Run ();
		return 0;
	}
}
