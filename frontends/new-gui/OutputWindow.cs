using Gtk;
using System;
using System.IO;
using System.Text;
using Pango;

namespace Mono.Debugger.GUI {

	// <summary>
	//   This is a Gtk.TextView which can be used to display debugging output.  It is
	//   derived from System.IO.TextWriter so that you can just write to the output
	//   window.
	// </summary>
	public class OutputWindow : DebuggerTextWriter
	{
		HBox hbox;
		GUIContext context;
		DebuggerTerminal term;

		public OutputWindow (GUIContext context)
		{
			this.context = context;

			hbox = new Gtk.HBox (false, 8);
			hbox.Spacing = 2;

			term = new DebuggerTerminal ();
			term.Blink = true;
			term.Bell = true;
			term.Scrollback = 500;
			term.ScrollOnKeystroke = false;
			term.ScrollOnOutput = false;
			// term.FontName = "-*-courier-medium-r-normal--18-*-*-*-*-*-*-*";

			Gtk.VScrollbar scrollbar = new Gtk.VScrollbar (term.Vadjustment);
			hbox.PackStart (term, true, true, 0);
			hbox.PackStart (scrollbar, false, true, 0);
			hbox.ShowAll ();
		}

		public Widget Widget {
			get { return hbox; }
		}

		// <summary>
		//   The TextWriter's main output function.
		// </summary>
                public override void Write (bool is_stderr, string output)
		{
			context.Lock ();

			term.Feed (output);

			context.UnLock ();
		}
	}
}
