using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class CurrentInstructionEntry : DebuggerWidget
	{
		Gtk.Entry entry;
		string current_insn = null;

		public CurrentInstructionEntry (Gtk.Entry widget)
			: base (widget)
		{
			entry = widget;
			entry.Sensitive = false;
		}

		public override void SetBackend (DebuggerBackend backend, Process process)
		{
			base.SetBackend (backend, process);
			process.StateChanged += new StateChangedHandler (StateChanged);
			process.FrameChangedEvent += new StackFrameHandler (FrameChanged);
			process.FramesInvalidEvent += new StackFrameInvalidHandler (FramesInvalid);
		}

		protected void Update ()
		{
			entry.Text = current_insn;
			widget.Sensitive = current_insn != null;
		}

		// <remarks>
		//   This method is called from the SingleSteppingEngine's background thread,
		//   so we must not use gtk# here.
		// </remarks>
		void FrameChanged (StackFrame frame)
		{
			try {
				IDisassembler dis = process.Disassembler;
				TargetAddress old_addr = frame.TargetAddress;
				TargetAddress addr = old_addr;
				string insn = dis.DisassembleInstruction (ref addr);
				current_insn = String.Format ("0x{0:x}   {1}", old_addr.Address, insn);
			} catch (Exception e) {
				Console.WriteLine (e);
				current_insn = null;
			}
		}

		void FramesInvalid ()
		{
			current_insn = null;
		}
		
		public void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.STOPPED:
				Update ();
				break;

			default:
				widget.Sensitive = false;
				break;
			}
		}
	}
}
