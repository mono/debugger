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

	public class SourceList {
		static Gdk.Pixbuf line, stop;

		static SourceList ()
		{
			stop = new Gdk.Pixbuf (null, "stop.png");
			line = new Gdk.Pixbuf (null, "line.png");
		}

		SourceManager manager;			
		ScrolledWindow sw;
		Gtk.SourceView source_view;
		TextTag frame_tag, breakpoint_tag;
		Gtk.SourceBuffer text_buffer;

		ClosableNotebookTab tab;
		SourceFileFactory factory;
		DebuggerBackend backend;
		Process process;
		
		bool active;
		string filename;

		Hashtable breakpoints = new Hashtable ();
		
		public SourceList (SourceManager manager, ISourceBuffer source_buffer, string filename)
		{
			this.manager = manager;
			this.backend = manager.DebuggerBackend;
			Console.WriteLine ("Filename: " + filename);
			this.filename = filename;
			tab = new ClosableNotebookTab (filename);

			factory = new SourceFileFactory ();
			
			sw = new ScrolledWindow (null, null);
			sw.SetPolicy (PolicyType.Automatic, PolicyType.Automatic);
			text_buffer = new Gtk.SourceBuffer (new Gtk.TextTagTable ());
			source_view = new Gtk.SourceView (text_buffer);
			source_view.ButtonPressEvent += new ButtonPressEventHandler (button_pressed);
			source_view.Editable = false;

			FontDescription font = FontDescription.FromString ("Monospace 14");
			source_view.ModifyFont (font);

			//
			// The sourceview tags we use.
			//
			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "yellow";
			text_buffer.CreateMark ("frame", text_buffer.StartIter, true);
			text_buffer.TagTable.Add (frame_tag);

			breakpoint_tag = new Gtk.TextTag ("bpt");
			breakpoint_tag.Background = "red";
			text_buffer.CreateMark ("bpt", text_buffer.StartIter, true);
			text_buffer.TagTable.Add (breakpoint_tag);

			//
			// Load contents
			//
			string contents = GetSource (source_buffer);
			text_buffer.Text = contents;

			//
			// Load our markers
			//
			source_view.AddPixbuf ("stop", stop, false);
			source_view.AddPixbuf ("line", line, false);
			
			sw.Add (source_view);
			sw.ShowAll ();

			//
			// Hook up events
			//
			manager.FrameChangedEvent += new StackFrameHandler (frame_changed_event);
			manager.FramesInvalidEvent += new StackFrameInvalidHandler (frame_invalid_event);
			manager.MethodInvalidEvent += new MethodInvalidHandler (method_invalid_event);
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
					Breakpoint bp = new SimpleBreakpoint (String.Format ("{0}:{1}", filename, line));
					int id = backend.InsertBreakpoint (bp, filename, line);

					text_buffer.LineAddMarker (line, "stop");
					breakpoints [line] = id;
				}
			} 
		}
		
		string GetSource (ISourceBuffer buffer)
		{
			if (buffer.HasContents)
				return buffer.Contents;

			if (factory == null) {
				Console.WriteLine (
					"I don't have a SourceFileFactory, can't lookup source code.");
				return null;
			}

			SourceFile file = factory.FindFile (buffer.Name);
			if (file == null) {
				Console.WriteLine ("Can't find source file {0}.", buffer.Name);
				return null;
			}

			return file.Contents;
		}

		public void SetProcess (Process process)
		{
			this.process = process;
		}

		public bool Active {
			get {
				return active;
			}

			set {
				if (active == value)
					return;
				active = value;
				if (!active) {
					current_frame = null;
					text_buffer.RemoveTag (
						frame_tag, text_buffer.StartIter, text_buffer.EndIter);
				} else
					frame_changed_event (process.CurrentFrame);
			}
		}

		void ClearLine ()
		{
			if (last_line != -1){
				text_buffer.LineRemoveMarker (last_line, "line");
				last_line = -1;
			}

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}
		
		void method_invalid_event ()
		{
			Active = false;
			ClearLine ();
		}

		void frame_invalid_event ()
		{
			ClearLine ();
		}

		StackFrame current_frame = null;
		int last_line = 0;
		
		void frame_changed_event (StackFrame frame)
		{
			if (!active || (frame == current_frame))
				return;

			current_frame = frame;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			if (!active)
				return;

			SourceLocation source = frame.SourceLocation;
			if (source == null)
				return;

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, source.Row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, source.Row, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			if (last_line != -1)
				text_buffer.LineRemoveMarker (last_line, "line");
			
			text_buffer.LineAddMarker (source.Row, "line");
			last_line = source.Row;
			
			Gtk.TextMark frame_mark = text_buffer.GetMark ("frame");
			text_buffer.MoveMark (frame_mark, start_iter);
			source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
		
		public ClosableNotebookTab TabWidget {
			get {
				return tab;
			}
		}

		public Widget ToplevelWidget {
			get {
				return sw;
			}
		}
	}
}
