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

		public TargetStatusbar (IDebuggerBackend backend, Gtk.Statusbar widget)
			: base (backend, widget)
		{
			status_bar = widget;
			status_id = status_bar.GetContextId ("message");
			backend.StateChanged += new StateChangedHandler (StateChanged);
		}

		public void Message (string message)
		{
			status_bar.Pop (status_id);
			status_bar.Push (status_id, message);
			MainIteration ();
		}

		public virtual void StateChanged (TargetState new_state)
		{
			switch (new_state) {
			case TargetState.RUNNING:
				Message ("Running ....");
				break;

			case TargetState.STOPPED:
				try {
					if (backend.Inferior != null) {
						ITargetLocation frame = backend.Inferior.CurrentFrame;
						Message (String.Format ("Stopped at {0}.", frame));
					} else
						Message ("Stopped (no target to debug).");
				} catch (NoStackException e) {
					Message ("Stopped.");
				} catch (Exception e) {
					Console.WriteLine (e);
					Message ("Stopped (can't get current stackframe).");
				}
				break;

			case TargetState.EXITED:
				Message ("Program terminated.");
				break;

			case TargetState.NO_TARGET:
				Message ("No target to debug.");
				break;

			case TargetState.BUSY:
				Message ("Debugger busy ...");
				break;
			}
		}
	}
}
