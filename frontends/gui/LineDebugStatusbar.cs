using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class LineDebugStatusbar : TargetStatusbar
	{
		public LineDebugStatusbar (DebuggerGUI gui, Gtk.Statusbar widget)
			: base (gui, widget)
		{
		}

		protected override void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			if ((new_state != TargetState.STOPPED) || (arg != 0)) {
				Message ("");
				return;
			}

			SourceLocation source = null;
			TargetAddress address;
			try {
				StackFrame frame = process.CurrentFrame;
				source = frame.SourceLocation;
				address = frame.TargetAddress;
			} catch {
				Message ("");
				return;
			}

			if (source == null) {
				Message ("");
				return;
			}

			TargetAddress start = address - source.SourceOffset;
			TargetAddress end = address + source.SourceRange;

			string dis_message;
			IDisassembler dis = process.Disassembler;
			if (dis != null) {
				TargetAddress current = start;
				while (current < end)
					current += dis.GetInstructionSize (current);
				if (current == end)
					dis_message = "End address ok.";
				else
					dis_message = String.Format (
						"Symfile has end address {0}, but disassembler tells us {1}.",
						end, current);
			} else
				dis_message = "No disassembler.";

			string message = String.Format ("Stopped at {0}.\n" +
							"Source line from {2} to {3}.\n" +
							"Target address is {1}.\n{4}",
							source, address, start, end, dis_message);

			Message (message);
		}
	}
}
