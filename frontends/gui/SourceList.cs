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

		ClosableNotebookTab tab;
		string filename;

		Hashtable breakpoints = new Hashtable ();

		protected override Gtk.Widget CreateWidget (Gtk.SourceView source_view)
		{
			Gtk.ScrolledWindow sw = new ScrolledWindow (null, null);
			sw.SetPolicy (PolicyType.Automatic, PolicyType.Automatic);
			sw.Add (source_view);
			return sw;
		}
		
		public SourceList (SourceManager manager, string filename, string contents)
			: base (manager)
		{
			Console.WriteLine ("Filename: " + filename);
			this.filename = filename;

			tab = new ClosableNotebookTab (filename);

			source_view.ButtonPressEvent += new ButtonPressEventHandler (button_pressed);

			//
			// Load contents
			//
			text_buffer.Text = contents;
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

		public ClosableNotebookTab TabWidget {
			get {
				return tab;
			}
		}

		public override void InsertBreakpoint ()
		{
			Console.WriteLine ("INFO: Implement me, insert a breakpoint here");
		}
	}
}
