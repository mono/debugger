using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Pango;
namespace Mono.Debugger.GUI
{
	public abstract class SourceView : DebuggerWidget
	{
		protected Gtk.TextView source_view;
		protected Gtk.TextBuffer text_buffer;
		protected Gtk.TextTag frame_tag;
		protected SourceFileFactory factory;

		public SourceView (DebuggerGUI gui, Gtk.Container container, Gtk.TextView widget)
			: base (gui, container, widget)
		{
			source_view = widget;
			FontDescription font = FontDescription.FromString ("Monospace");
			source_view.ModifyFont (font);

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text_buffer = source_view.Buffer;
			text_buffer.TagTable.Add (frame_tag);

			text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

			factory = new SourceFileFactory ();
		}

		protected override void MethodInvalid ()
		{
			if (!IsVisible)
				return;

			text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);
		}

		protected override void MethodChanged (IMethod method, IMethodSource source)
		{
			if (!IsVisible)
				return;

			text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

			if (method == null)
				return;

			ISourceBuffer buffer = source.SourceBuffer;
			if (buffer == null) {
				Console.WriteLine ("The buffer is empty");
				return;
			}

			if (buffer.HasContents) {
				text_buffer.Insert (
					text_buffer.EndIter, buffer.Contents, buffer.Contents.Length);
				return;
			}

			if (factory == null) {
				Console.WriteLine (
					"I don't have a SourceFileFactory, can't lookup source code.");
				return;
			}

			SourceFile file = factory.FindFile (buffer.Name);
			if (file == null) {
				Console.WriteLine ("Can't find source file {0}.", buffer.Name);
				return;
			}

			text_buffer.Insert (text_buffer.EndIter, file.Contents, file.Contents.Length);
		}

		protected override void FramesInvalid ()
		{
			if (!IsVisible)
				return;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		protected virtual SourceLocation GetSource (StackFrame frame)
		{
			if (CurrentMethodSource == null)
				return null;

			return CurrentMethodSource.Lookup (frame.TargetAddress);
		}

		protected override void FrameChanged (StackFrame frame)
		{
			if (!IsVisible)
				return;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			SourceLocation source = GetSource (frame);
			if (source == null)
				return;

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
