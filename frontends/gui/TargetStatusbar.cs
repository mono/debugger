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

		public TargetStatusbar (Gtk.Statusbar widget)
			: base (widget)
		{
			status_bar = widget;
			status_id = status_bar.GetContextId ("message");
		}

		public override void SetBackend (DebuggerBackend backend, Process process)
		{
			base.SetBackend (backend, process);
			process.StateChanged += new StateChangedHandler (StateChanged);
		}
		
		public void Message (string message)
		{
			if (!IsVisible)
				return;

			status_bar.Pop (status_id);
			status_bar.Push (status_id, message);
			MainIteration ();
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

		public virtual void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.RUNNING:
				Message ("Running ....");
				break;

			case TargetState.CORE_FILE:
			case TargetState.STOPPED:
				try {
					StackFrame frame = process.CurrentFrame;
					Message (GetStopMessage (frame, arg));
				} catch (NoStackException) {
					Message (String.Format ("{0}.", GetStopReason (arg)));
				} catch (Exception e) {
					Console.WriteLine (e);
					Message (String.Format ("{0} ({1}).", GetStopReason (arg),
								"(can't get current stackframe)"));
				}
				break;

			case TargetState.EXITED:
				if (arg == 0)
					Message ("Program terminated.");
				else
					Message (String.Format ("Program terminated with signal {0}.", arg));
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
