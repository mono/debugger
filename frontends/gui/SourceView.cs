using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class SourceView : DebuggerWidget
	{
		Gtk.TextView source_view;
		Gtk.TextBuffer text_buffer;
		Gtk.TextTag frame_tag;
		Gtk.TextMark frame_mark;

		bool has_frame;

		public SourceView (IDebuggerBackend backend, Gtk.TextView widget)
			: base (backend, widget)
		{
			source_view = widget;

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text_buffer = source_view.Buffer;
			text_buffer.TagTable.Add (frame_tag);

			frame_mark = text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFramesInvalidHandler (FramesInvalidEvent);
		}

		ISourceBuffer current_buffer = null;

		void FramesInvalidEvent ()
		{
			has_frame = false;
			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		void FrameChangedEvent (IStackFrame frame)
		{
			has_frame = true;

			if ((frame.SourceLocation == null) || (frame.SourceLocation.Buffer == null))
				return;

			Gtk.TextBuffer buffer = source_view.Buffer;

			ISourceBuffer source_buffer = frame.SourceLocation.Buffer;
			int row = frame.SourceLocation.Row;

			if (current_buffer != source_buffer) {
				current_buffer = source_buffer;

				text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

				text_buffer.Insert (text_buffer.EndIter, source_buffer.Contents,
						    source_buffer.Contents.Length);
			}

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, row, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			text_buffer.MoveMark (frame_mark, start_iter);

			source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
	}
}
