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
			Prepend (menu, "_Duplicate this view", new EventHandler (DuplicateViewCB));
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

		public override void InsertBreakpoint (int line)
		{
			if (breakpoints [line] != null){
				int key = (int) breakpoints [line];

				manager.DebuggerBackend.RemoveBreakpoint (key);
				text_buffer.LineRemoveMarker (line, "stop");
				breakpoints [line] = null;
			} else {
				SourceLocation location = manager.FindLocation (filename, line);
				if (location == null){
					Report.Warning ("Cannot insert a breakpoint on {0}, line {1} " +
							"because there is no debugging information " +
							"available for this line.", filename, line);
					return;
				}

				Breakpoint bp = new SimpleBreakpoint (location.Name);
				int id = manager.DebuggerBackend.InsertBreakpoint (bp, location);

				text_buffer.LineAddMarker (line, "stop");
				breakpoints [line] = id;
			}
		}

		void button_pressed (object obj, ButtonPressEventArgs args)
		{
			Gdk.EventButton ev = args.Event;

			if (ev.window.Equals (source_view.GetWindow (TextWindowType.Left)))
				InsertBreakpoint (GetLineXY ((int) ev.x, (int) ev.y));
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
	}
}
