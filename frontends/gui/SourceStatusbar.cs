using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class SourceStatusbar : TargetStatusbar
	{
		public SourceStatusbar (IDebuggerBackend backend, Gtk.Statusbar widget)
			: base (backend, widget)
		{
		}

		public override void StateChanged (TargetState new_state)
		{
			switch (new_state) {
			case TargetState.STOPPED:
				try {
					IStackFrame frame = backend.CurrentFrame;
					Message (String.Format ("Stopped at {0}.", frame));
				} catch (NoStackException) {
					Message ("Stopped.");
				} catch (Exception e) {
					Console.WriteLine (e);
					Message ("Stopped (can't get current stackframe).");
				}
				break;

			default:
				base.StateChanged (new_state);
				break;
			}
		}
	}
}
