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
		protected IMethod current_method = null;
		protected IMethodSource current_method_source = null;
		protected SourceFileFactory factory;

		bool has_frame;

		public SourceView (Gtk.Container container, Gtk.TextView widget)
			: base (container, widget)
		{
			source_view = widget;

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text_buffer = source_view.Buffer;
			text_buffer.TagTable.Add (frame_tag);

			text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

			factory = new SourceFileFactory ();
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);
			
			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (FramesInvalidEvent);
			backend.MethodInvalidEvent += new MethodInvalidHandler (MethodInvalidEvent);
			backend.MethodChangedEvent += new MethodChangedHandler (MethodChangedEvent);
			backend.TargetExited += new TargetExitedHandler (TargetExitedEvent);
		}

		void TargetExitedEvent ()
		{
			MethodInvalidEvent ();
		}

		void MethodInvalidEvent ()
		{
			current_method = null;
			current_method_source = null;

			if (!IsVisible)
				return;

			text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);
		}

		void MethodChangedEvent (IMethod method)
		{
			current_method = method;
			current_method_source = null;

			if (!IsVisible)
				return;

			text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

			if (method == null)
				return;

			Console.WriteLine ("++++++++");
			current_method_source = GetMethodSource (method);
			if (current_method_source == null)
				return;

			ISourceBuffer buffer = current_method_source.SourceBuffer;
			if (buffer == null) {
				Console.WriteLine ("The buffer is empty");
				current_method_source = null;
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
				current_method_source = null;
				return;
			}

			SourceFile file = factory.FindFile (buffer.Name);
			if (file == null) {
				Console.WriteLine ("Can't find source file {0}.", buffer.Name);
				current_method_source = null;
				return;
			}

			text_buffer.Insert (text_buffer.EndIter, file.Contents, file.Contents.Length);
		}

		void FramesInvalidEvent ()
		{
			if (!IsVisible)
				return;

			has_frame = false;
			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		protected virtual IMethodSource GetMethodSource (IMethod method)
		{
			if ((method == null) || !method.HasSource)
				return null;

			return method.Source;
		}

		protected virtual SourceLocation GetSource (StackFrame frame)
		{
			if (current_method_source == null)
				return null;

			return current_method_source.Lookup (frame.TargetAddress);
		}

		void FrameChangedEvent (StackFrame frame)
		{
			if (!IsVisible)
				return;

			has_frame = true;

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			SourceLocation source = GetSource (frame);
			if (source == null)
				return;

			Console.WriteLine ("This one got source");

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
