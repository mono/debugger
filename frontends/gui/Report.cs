using Gtk;

namespace Mono.Debugger.GUI {

	public class Report {

		static public void Error (string msg)
		{
			MessageDialog d = new MessageDialog (null, 0, MessageType.Error, ButtonsType.Ok, msg);

			d.Run ();
			d.Destroy ();
		}
	}
}
