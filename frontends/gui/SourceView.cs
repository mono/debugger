using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class SourceView : DebuggerWidget
	{
		protected Gtk.TextView source_view;
		protected Gtk.TextBuffer text_buffer;
		protected Gtk.TextTag frame_tag;
		protected ISourceBuffer current_buffer = null;

		bool has_frame;

		public SourceView (IDebuggerBackend backend, Gtk.TextView widget)
			: base (backend, widget)
		{
			source_view = widget;

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text_buffer = source_view.Buffer;
			text_buffer.TagTable.Add (frame_tag);

			text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFramesInvalidHandler (FramesInvalidEvent);
		}

		void FramesInvalidEvent ()
		{
			has_frame = false;
			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		protected virtual ISourceLocation GetSource (IStackFrame frame)
		{
			return frame.SourceLocation;
		}

		void FrameChangedEvent (IStackFrame frame)
		{
			has_frame = true;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			ISourceLocation source = GetSource (frame);
			if (source == null)
				return;

			Gtk.TextBuffer buffer = source_view.Buffer;

			if (current_buffer != source.Buffer) {
				current_buffer = source.Buffer;

				text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

				text_buffer.Insert (text_buffer.EndIter, current_buffer.Contents,
						    current_buffer.Contents.Length);
			}

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, source.Row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, source.Row, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			Gtk.TextMark frame_mark = text_buffer.GetMark ("frame");

			text_buffer.MoveMark (frame_mark, start_iter);

			source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
	}
}
