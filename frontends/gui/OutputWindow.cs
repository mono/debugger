using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Pango;

namespace Mono.Debugger.GUI {

	// <summary>
	//   This is a Gtk.TextView which can be used to display debugging output.  It is
	//   derived from System.IO.TextWriter so that you can just write to the output
	//   window.
	// </summary>
	public class OutputWindow : DebuggerTextWriter
	{
		Gtk.TextView output_area;
		Gtk.TextBuffer output_buffer;
		Gtk.TextTag error_tag;
		Gtk.TextMark last_mark;

		public OutputWindow (Gtk.TextView output_area)
		{
			this.output_area = output_area;

			output_area.Editable = false;
			output_area.WrapMode = Gtk.WrapMode.None;
			output_buffer = output_area.Buffer;

			FontDescription font = FontDescription.FromString ("Monospace");
			output_area.ModifyFont (font);

			error_tag = new Gtk.TextTag ("error");
			error_tag.Foreground = "red";

			output_buffer.TagTable.Add (error_tag);

			last_mark = output_buffer.CreateMark ("last", output_buffer.StartIter, true);
		}

		// <summary>
		//   The widget to be embedded into the parent application.
		// </summary>
		public Gtk.Widget Widget {
			get {
				return output_area;
			}
		}

		// <summary>
		//   The TextWriter's main output function.
		// </summary>
                public override void Write (bool is_stderr, string output)
		{
			output_buffer.Insert (output_buffer.EndIter, output);

			if (is_stderr) {
				Gtk.TextIter start_iter;
				output_buffer.GetIterAtMark (out start_iter, last_mark);
				output_buffer.ApplyTag (error_tag, start_iter, output_buffer.EndIter);
			}
			output_buffer.MoveMark (last_mark, output_buffer.EndIter);
			output_area.ScrollToMark (last_mark, 0.4, true, 0.0, 1.0);
		}
	}
}
