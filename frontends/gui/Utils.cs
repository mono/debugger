//
// Utility classes and functions
//
// Author:
//    Miguel de Icaza (miguel@ximian.com)
//
// (C) 2002 Ximian, Inc.
//
// TODO:
//
//    I have attempted every possible combination to get this implemented,
//    and it is just too hard:
//
//	 * GtkButton, renders incorrectly when the tab is not selected.
//         Adds lots of spacing that I have not been able to remove, even
//         creating and customizing my own style.
//         If you use an image, things get even worse.
//
//       * EventBox: renders incorrectly.
//
//       * GtkImage, eventboxless: renders correctly, impossible to get events out.
//
// So, I have stuck to EventBox, which renders incorrectly, and currently does not
// have the proper "button" behavior.  It wont highlight, and it wont let you release
// the mouse before closing.  It will just close.  Right away, no questions asked.
//
using System;
using Gtk;
using GtkSharp;
using Gdk;

namespace Mono.Debugger.GUI {

	public class ClosableNotebookTab : HBox {
		public EventHandler ButtonClicked;
		EventBox eb;
		
		public ClosableNotebookTab (string name) : base (false, 0)
		{
			int idx = name.LastIndexOf ("/");

			if (idx > -1){
				name = name.Substring (idx + 1);
			}
			
			Gtk.Image close_image = new Gtk.Image (Stock.Close, IconSize.Menu);
			PackStart (new Label (name));
			eb =  new EventBox ();
			eb.Add (close_image);
			PackEnd (eb);

			eb.ButtonPressEvent += new ButtonPressEventHandler (button_pressed);
			eb.Events = (int)EventMask.ExposureMask | (int)EventMask.LeaveNotifyMask;
			ShowAll ();
		}

		void button_pressed (object obj, ButtonPressEventArgs args)
		{
			Gdk.EventButton ev = args.Event;
			if (ev.button == 1 && ButtonClicked != null)
				ButtonClicked (this, null);

			SignalArgs sa = (SignalArgs) args;
			sa.RetVal = true;
		}
	}

	public class Utils
	{
		public static string GetBasename (string filename)
		{
			int pos = filename.LastIndexOf ('/');
			if (pos < 0)
				return filename;
			return filename.Substring (pos+1);
		}
	}
}
