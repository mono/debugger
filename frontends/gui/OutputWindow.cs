using Gtk;
using System.IO;

namespace Mono.Debugger.GUI {

	// <summary>
	//   This is a Gtk.TextView which can be used to display debugging output.  It is
	//   derived from System.IO.TextWriter so that you can just write to the output
	//   window.
	// </summary>
	public class OutputWindow : TextWriter
	{
		Gtk.TextView output_area;
		Gtk.TextBuffer output_buffer;

		public OutputWindow ()
		{
			output_area = new Gtk.TextView ();

			output_area.Editable = false;
			output_area.WrapMode = Gtk.WrapMode.None;

			output_buffer = output_area.Buffer;
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
		//   This is inherited from TextWriter, we just return ASCII for the
		//   moment.
		// </summary>
		public override System.Text.Encoding Encoding {
			get {
				return System.Text.Encoding.ASCII;
			}
		}

		// <summary>
		//   The TextWriter's main output function.
		// </summary>
                public override void Write (string output)
		{
			output_buffer.Insert (output_buffer.EndIter, output, output.Length);
			output_area.ScrollToMark (output_buffer.InsertMark, 0.4, true, 0.0, 1.0);
		}
	}
}
