using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class SourceStatusbar : TargetStatusbar
	{
		public SourceStatusbar (Gtk.Statusbar widget)
			: base (widget)
		{
		}

		public override void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.STOPPED:
				try {
					IStackFrame frame = backend.CurrentFrame;
					Message (String.Format ("{1} at {0}.", frame, GetStopReason (arg)));
				} catch (NoStackException) {
					Message (String.Format ("{0}.", GetStopReason (arg)));
				} catch (Exception e) {
					Console.WriteLine (e);
					Message (String.Format ("{0} ({1}).", GetStopReason (arg),
								"(can't get current stackframe)"));
				}
				break;

			default:
				base.StateChanged (new_state, arg);
				break;
			}
		}
	}
}
