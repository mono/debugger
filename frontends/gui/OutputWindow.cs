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
		DebuggerTerminal term;
		ThreadNotify thread_notify;
		int notify_id;

		public OutputWindow (DebuggerGUI gui, Gtk.Container container)
		{
			Gtk.HBox hbox = new Gtk.HBox (false, 0);
			hbox.Spacing = 2;
			container.BorderWidth = 2;
			container.Add (hbox);

			term = new DebuggerTerminal ();
			term.Blink = true;
			term.Bell = true;
			term.Scrollback = 500;
			term.ScrollOnKeystroke = false;
			term.ScrollOnOutput = false;
			term.FontName = "-*-courier-medium-r-normal--18-*-*-*-*-*-*-*";

			Gtk.VScrollbar scrollbar = new Gtk.VScrollbar (term.Vadjustment);
			hbox.PackStart (term, true, true, 0);
			hbox.PackStart (scrollbar, false, true, 0);
			hbox.ShowAll ();

			thread_notify = gui.ThreadNotify;
			notify_id = thread_notify.RegisterListener (new ReadyEventHandler (output_event));
		}

		void output_event ()
		{
			lock (this) {
				if (sb == null)
					return;

				term.Feed (sb.ToString ());
				sb = null;
				signaled = false;
			}
		}

		StringBuilder sb = null;
		bool signaled = false;

		// <summary>
		//   The TextWriter's main output function.
		// </summary>
                public override void Write (bool is_stderr, string output)
		{
			lock (this) {
				if (sb == null)
					sb = new StringBuilder ();
				sb.Append (output);
				if (!signaled) {
					thread_notify.Signal (notify_id);
					signaled = true;
				}
			}
		}
	}
}
