using Gtk;
using System;

namespace Mono.Debugger.GUI {

	public class Report {

		static public void Error (string msg)
		{
			MessageDialog d = new MessageDialog (null, 0, MessageType.Error, ButtonsType.Ok, msg);

			d.Run ();
			d.Destroy ();
		}

		static public void Error (string format, params object[] args)
		{
			Error (String.Format (format, args));
		}

		static public void Warning (string msg)
		{
			MessageDialog d = new MessageDialog (null, 0, MessageType.Warning, ButtonsType.Ok, msg);

			d.Run ();
			d.Destroy ();
		}

		static public void Warning (string format, params object[] args)
		{
			Warning (String.Format (format, args));
		}
	}
}
