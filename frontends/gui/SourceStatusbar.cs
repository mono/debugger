using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class SourceStatusbar : TargetStatusbar
	{
		public SourceStatusbar (DebuggerGUI gui, string glade_name)
			: this (gui, (Gtk.Statusbar) gui.GXML [glade_name])
		{ }

		public SourceStatusbar (DebuggerGUI gui, Gtk.Statusbar widget)
			: base (gui, widget)
		{
		}

		bool is_source_status = false;
		TargetEventArgs current_state = null;

		public bool IsSourceStatusBar {
			get {
				return is_source_status;
			}

			set {
				is_source_status = value;
				if (current_state != null)
					OnTargetEvent (current_state);
			}
		}

		protected override void OnTargetEvent (TargetEventArgs args)
		{
			this.current_state = args;

			if (!IsVisible)
				return;

			if (!is_source_status) {
				base.OnTargetEvent (args);
				return;
			}

			switch (args.Type) {
			case TargetEventType.TargetStopped:
				if (args.Frame == null) {
					Message ("{0}.", GetStopReason ((int) args.Data));
					break;
				}
				if (args.Frame.SourceAddress == null) {
					base.OnTargetEvent (args);
					return;
				}
				string filename = Utils.GetBasename (args.Frame.SourceAddress.Name);
				string offset = "";
				if (args.Frame.SourceAddress.SourceOffset > 0)
					offset = String.Format (
						" (offset 0x{0})", args.Frame.SourceAddress.SourceOffset);
				Message ("{0} at {1}{3} at {2}.", GetStopReason ((int) args.Data),
					 filename, args.Frame.TargetAddress, offset);
				break;

			default:
				base.OnTargetEvent (args);
				break;
			}
		}
	}
}
