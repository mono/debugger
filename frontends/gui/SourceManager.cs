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
				source_view.GetLineAtY (out iter, buffer_y, 0);
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
	
	public class SourceManager : DebuggerWidget {
		Hashtable sources; 
		SourceStatusbar source_status;
		Gtk.Notebook notebook;
		bool initialized;

		//
		// State tracking
		//
		SourceList current_source = null;
		
		public SourceManager (DebuggerGUI gui, Gtk.Notebook notebook, SourceStatusbar source_status)
			: base (gui, notebook)
		{
			sources = new Hashtable ();
			this.gui = gui;
			this.notebook = notebook;
			this.source_status = source_status;

			notebook.SwitchPage += new SwitchPageHandler (switch_page);
		}
		
		public override void SetProcess (Process process)
		{
			base.SetProcess (process);

			foreach (DictionaryEntry de in sources){
				SourceList source = (SourceList) de.Value;

				source.SetProcess (process);
			}
		}

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		SourceList CreateSourceView (ISourceBuffer source_buffer, string filename)
		{
			return new SourceList (this, source_buffer, filename);
		}

		int GetPageIdx (Gtk.Widget w)
		{
			Widget v;
			int i = 0;
			
			do {
				v = notebook.GetNthPage (i);
				if (v != null){
					if (w.Equals (v))
						return i;
				}
				i++;
			} while (v != null);
			return -1;
		}
		
		void close_tab (object o, EventArgs args)
		{
			foreach (DictionaryEntry de in sources){
				string name = (string) de.Key;
				SourceList view = (SourceList) de.Value;

				if (view.TabWidget == o){
					Widget view_widget = view.ToplevelWidget;
					Widget v;
					int i = 0;

					do {
						v = notebook.GetNthPage (i);
						Console.WriteLine ("trying: {0} vs {1}", view_widget, v);
						if (view_widget.Equals (v)){
							notebook.RemovePage (i);
							sources [name] = null;
							return;
						}
						i++;
					} while (v != null);
				}
			}
		}

		void switch_page (object o, SwitchPageArgs args)
		{
			source_status.IsSourceStatusBar = args.PageNum != 0;
		}

		protected override void FrameChanged (StackFrame frame)
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		protected override void FramesInvalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		protected override void MethodInvalid ()
		{
			if (current_source != null)
				current_source.Active = false;
			current_source = null;

			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
		}
		
		protected override void MethodChanged (IMethod method, IMethodSource source)
		{
			MethodInvalid ();

			if (source != null){
				ISourceBuffer source_buffer = source.SourceBuffer;

				if (source_buffer == null) {
					current_source = null;
					goto done;
				}

				string filename = source_buffer.Name;

				SourceList view = (SourceList) sources [filename];
			
				if (view == null){
					view = CreateSourceView (source_buffer, filename);
					if (process != null)
						view.SetProcess (process);
					
					sources [filename] = view;
					notebook.InsertPage (view.ToplevelWidget, view.TabWidget, -1);
					notebook.SetMenuLabelText (view.ToplevelWidget, filename);
					view.TabWidget.ButtonClicked += new EventHandler (close_tab);
				}

				view.Active = true;

				if (!initialized || (notebook.Page != 0)) {
					int idx = GetPageIdx (view.ToplevelWidget);
					if (idx != -1)
						notebook.Page = idx;
				}

				current_source = view;
			} else {
				Console.WriteLine ("********* Need to show disassembly **********");
				current_source = null;
			}

		done:
			initialized = true;
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}
	}
}
