using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class SourceStatusbar : TargetStatusbar
	{
		public SourceStatusbar (DebuggerGUI gui, Gtk.Statusbar widget)
			: base (gui, widget)
		{
		}

		bool is_source_status = false;
		TargetState state;
		int arg;

		public bool IsSourceStatusBar {
			get {
				return is_source_status;
			}

			set {
				is_source_status = value;
				StateChanged (state, arg);
			}
		}

		protected override void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			this.state = new_state;
			this.arg = arg;

			if (!is_source_status) {
				base.StateChanged (new_state, arg);
				return;
			}

			switch (new_state) {
			case TargetState.CORE_FILE:
			case TargetState.STOPPED:
				if (CurrentFrame == null) {
					Message (String.Format ("{0}.", GetStopReason (arg)));
					break;
				}
				if (CurrentFrame.SourceLocation == null) {
					base.StateChanged (new_state, arg);
					return;
				}
				string filename = Utils.GetBasename (CurrentFrame.SourceLocation.Name);
				string offset = "";
				if (CurrentFrame.SourceLocation.SourceOffset > 0)
					offset = String.Format (
						" (offset 0x{0})", CurrentFrame.SourceLocation.SourceOffset);
				Message (String.Format ("{0} at {1}{3} at {2}.", GetStopReason (arg),
							filename, CurrentFrame.TargetAddress, offset));
				break;

			default:
				base.StateChanged (new_state, arg);
				break;
			}
		}
	}
}
