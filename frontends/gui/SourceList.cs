//
// SourceManager: The source code manager
//
// This takes care of loading and showing the proper source code file
//
//

using System;
using System.Collections;
using Gtk;
using GtkSharp;
using Pango;

namespace Mono.Debugger.GUI {

	public class SourceList : SourceView {
		static Gdk.Pixbuf line, stop;

		static SourceList ()
		{
			stop = new Gdk.Pixbuf (null, "stop.png");
			line = new Gdk.Pixbuf (null, "line.png");
		}

		Label tab_label;
		Widget tab_widget;
		string filename;

		Hashtable breakpoints = new Hashtable ();

		protected override Gtk.Widget CreateWidget (Gtk.SourceView source_view)
		{
			Gtk.ScrolledWindow sw = new ScrolledWindow (null, null);
			sw.SetPolicy (PolicyType.Automatic, PolicyType.Automatic);
			sw.Add (source_view);
			return sw;
		}
		
		public SourceList (SourceManager manager, string name, string filename, string contents)
			: base (manager)
		{
			this.filename = filename;

			ClosableNotebookTab tab = new ClosableNotebookTab (name);
			tab.ButtonClicked += new EventHandler (close_tab);
			tab_widget = tab;

			source_view.ButtonPressEvent += new ButtonPressEventHandler (button_pressed);
		}

		public SourceList (SourceManager manager)
			: base (manager)
		{
			tab_widget = tab_label = new Label ("");

			source_view.ButtonPressEvent += new ButtonPressEventHandler (button_pressed);
		}

		public void SetContents (string filename, string name, string contents)
		{
			this.filename = filename;
			if (tab_label != null)
				tab_label.Text = name;

			text_buffer.BeginNotUndoableAction ();
			text_buffer.Text = contents;
			text_buffer.EndNotUndoableAction ();
		}

		protected override void PopulateViewPopup (Gtk.Menu menu)
		{
			Prepend (menu, "_Duplicate View", new EventHandler (DuplicateViewCB));
			base.PopulateViewPopup (menu);
		}

		void DuplicateViewCB (object o, EventArgs a)
		{
			manager.LoadFile (filename);
		}

		public EventHandler CloseEvent;

		void close_tab (object obj, EventArgs args)
		{
			if (CloseEvent != null)
				CloseEvent (this, null);
		}

		void button_pressed (object obj, ButtonPressEventArgs args)
		{
			Gdk.EventButton ev = args.Event;

			if (ev.window.Equals (source_view.GetWindow (TextWindowType.Left))){
				int buffer_x, buffer_y;

				source_view.WindowToBufferCoords (TextWindowType.Left, (int) ev.x, (int) ev.y, out buffer_x, out buffer_y);
				Gtk.TextIter iter;
				int line_top;
				source_view.GetLineAtY (out iter, buffer_y, out line_top);
				int line = iter.Line + 1;

				if (breakpoints [line] != null){
					int key = (int) breakpoints [line];

					backend.RemoveBreakpoint (key);
					text_buffer.LineRemoveMarker (line, "stop");
					breakpoints [line] = null;
				} else {
					SourceLocation location = manager.FindLocation (filename, line);
					if (location == null){
						Console.WriteLine ("Info: The manager was unable to find debugging info for `{0}' `{1}'", filename, line);
						return;
					}

					Breakpoint bp = new SimpleBreakpoint (location.Name);
					int id = backend.InsertBreakpoint (bp, location);

					text_buffer.LineAddMarker (line, "stop");
					breakpoints [line] = id;
				}
			} 
		}

		protected override SourceAddress GetSourceAddress (StackFrame frame)
		{
			return frame.SourceAddress;
		}

		public Widget TabWidget {
			get {
				return tab_widget;
			}
		}

		public override void InsertBreakpoint ()
		{
			Console.WriteLine ("INFO: Implement me, insert a breakpoint here");
		}
	}
}
