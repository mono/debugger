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
			if (!IsVisible)
				return;

			status_bar.Pop (status_id);
			status_bar.Push (status_id, message);
			MainIteration ();
		}

		protected virtual string GetStopMessage (IStackFrame frame)
		{
			if (frame.Method != null) {
				long offset = frame.TargetAddress - frame.Method.StartAddress;

				if (offset > 0)
					return String.Format ("Stopped at {0} in {1}+{2:x}",
							      frame.TargetAddress, frame.Method.Name,
							      offset);
				else if (offset == 0)
					return String.Format ("Stopped at {0} in {1}",
							      frame.TargetAddress, frame.Method.Name);
			}

			return String.Format ("Stopped at {0}.", frame.TargetAddress);
		}

		public virtual void StateChanged (TargetState new_state)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.RUNNING:
				Message ("Running ....");
				break;

			case TargetState.STOPPED:
				try {
					IStackFrame frame = backend.CurrentFrame;
					Message (GetStopMessage (frame));
				} catch (NoStackException) {
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
