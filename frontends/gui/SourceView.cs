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

	public abstract class SourceView {
		static Gdk.Pixbuf line, stop;

		static SourceView ()
		{
			stop = new Gdk.Pixbuf (null, "stop.png");
			line = new Gdk.Pixbuf (null, "line.png");
		}

		protected SourceManager manager;
		protected DebuggerBackend backend;
		protected Process process;

		protected Gtk.Container container;
		protected Gtk.SourceView source_view;
		protected TextTag frame_tag, breakpoint_tag;
		protected Gtk.SourceBuffer text_buffer;
		
		bool active;

		Hashtable breakpoints = new Hashtable ();
		
		public SourceView (SourceManager manager, Gtk.Container container)
		{
			this.manager = manager;
			this.backend = manager.DebuggerBackend;
			this.container = container;

			text_buffer = new Gtk.SourceBuffer (new Gtk.TextTagTable ());
			source_view = new Gtk.SourceView (text_buffer);
			source_view.Editable = false;

			//FontDescription font = FontDescription.FromString ("Monospace 14");
			//source_view.ModifyFont (font);

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
			// Load our markers
			//
			source_view.AddPixbuf ("stop", stop, false);
			source_view.AddPixbuf ("line", line, false);
			
			container.Add (source_view);
			container.ShowAll ();

			//
			// Hook up events
			//
			manager.FrameChangedEvent += new StackFrameHandler (frame_changed_event);
			manager.FramesInvalidEvent += new StackFrameInvalidHandler (frame_invalid_event);
			manager.MethodInvalidEvent += new MethodInvalidHandler (method_invalid_event);
			manager.TargetExitedEvent += new TargetExitedHandler (target_exited_event);
			manager.ProgramLoadedEvent += new ProgramLoadedHandler (program_loaded);

			process = manager.Process;
		}

		void program_loaded (object sender, Process process)
		{
			SetProcess (process);
		}

		protected void SetProcess (Process process)
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
				if (process == null)
					return;
				if (active)
					SetActive ();
				else
					SetInactive ();
			}
		}

		protected virtual void SetActive ()
		{
			frame_changed_event (process.CurrentFrame);
		}

		protected virtual void SetInactive ()
		{
			frame_invalid_event ();
		}

		protected void ClearLine ()
		{
			if (last_line != -1){
				text_buffer.LineRemoveMarker (last_line, "line");
				last_line = -1;
			}

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		void target_exited_event ()
		{
			Active = false;
			ClearLine ();
			process = null;
		}
		
		void method_invalid_event ()
		{
			ClearLine ();
		}

		void frame_invalid_event ()
		{
			current_frame = null;
			ClearLine ();
		}

		StackFrame current_frame = null;
		int last_line = 0;

		protected abstract SourceLocation GetSourceLocation (StackFrame frame);
		
		void frame_changed_event (StackFrame frame)
		{
			if (!active || (frame == current_frame))
				return;

			current_frame = frame;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			SourceLocation source = GetSourceLocation (frame);
			if (source == null)
				return;

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, source.Row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, source.Row, 0);

			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			if (last_line != -1)
				text_buffer.LineRemoveMarker (last_line, "line");
			
			text_buffer.LineAddMarker (source.Row, "line");
			last_line = source.Row;
			
			Gtk.TextMark frame_mark = text_buffer.GetMark ("frame");
			text_buffer.MoveMark (frame_mark, start_iter);
			source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
		
		public Widget ToplevelWidget {
			get {
				return container;
			}
		}
	}
}
