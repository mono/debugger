using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class TargetStatusbar : DebuggerWidget
	{
		protected Gtk.Statusbar status_bar;
		protected uint status_id;

		public TargetStatusbar (DebuggerGUI gui, Gtk.Statusbar widget)
			: base (gui, widget)
		{
			status_bar = widget;
			status_id = status_bar.GetContextId ("message");
		}

		public void Message (string message)
		{
			if (!IsVisible)
				return;

			status_bar.Pop (status_id);
			status_bar.Push (status_id, message);
		}

		public void Message (string format, params object[] args)
		{
			Message (String.Format (format, args));
		}

		protected virtual string GetStopReason (int arg)
		{
			if (arg == 0)
				return "Stopped";
			else
				return String.Format ("Received signal {0}", arg);
		}

		protected virtual string GetStopMessage (StackFrame frame, int arg)
		{
			if (frame.Method != null) {
				long offset = frame.TargetAddress - frame.Method.StartAddress;

				if (offset > 0)
					return String.Format ("{3} at {0} in {1}+{2:x}",
							      frame.TargetAddress, frame.Method.Name,
							      offset, GetStopReason (arg));
				else if (offset == 0)
					return String.Format ("{2} at {0} in {1}",
							      frame.TargetAddress, frame.Method.Name,
							      GetStopReason (arg));
			}

			return String.Format ("{1} at {0}.", frame.TargetAddress, GetStopReason (arg));
		}

		protected override void OnTargetEvent (TargetEventArgs args)
		{
			if (!IsVisible)
				return;

			switch (args.Type) {
			case TargetEventType.TargetRunning:
				Message ("Running ....");
				break;

			case TargetEventType.TargetStopped:
				if (args.Frame != null)
					Message (GetStopMessage (args.Frame, (int) args.Data));
				else
					Message ("{0}.", GetStopReason ((int) args.Data));
				break;

			case TargetEventType.TargetHitBreakpoint:
				Message ("Program hit breakpoint.");
				break;

			case TargetEventType.TargetExited:
				if ((int) args.Data == 0)
					Message ("Program terminated.");
				else
					Message ("Program terminated with exit code {0}.", (int) args.Data);
				break;

			case TargetEventType.TargetSignaled:
				Message ("Program died with fatal signal {0}.", (int) args.Data);
				break;

			default:
				Message ("Ooops, unknown target state {0}.", args.Type);
				break;
			}
		}
	}
}
