using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

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

		[DllImport("glib-2.0")]
		static extern bool g_main_context_iteration (IntPtr context, bool may_block);

		public OutputWindow (Gtk.TextView output_area)
		{
			this.output_area = output_area;
			this.output_area.Editable = false;
			this.output_area.WrapMode = Gtk.WrapMode.None;

			output_buffer = this.output_area.Buffer;
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
			while (g_main_context_iteration (IntPtr.Zero, false))
				;
		}
	}
}
