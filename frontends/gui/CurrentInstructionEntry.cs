using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class CurrentInstructionEntry : DebuggerWidget
	{
		Gtk.Entry entry;

		public CurrentInstructionEntry (Gtk.Entry widget)
			: base (widget)
		{
			entry = widget;
			entry.Sensitive = false;
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);
			backend.StateChanged += new StateChangedHandler (StateChanged);
			backend.FrameChangedEvent += new StackFrameHandler (FrameChanged);
		}

		protected void Update (TargetAddress frame)
		{
			if (!backend.HasTarget || (backend.Disassembler == null)) {
				widget.Sensitive = false;
				return;
			}

			try {
				IDisassembler dis = backend.Disassembler;
				TargetAddress old_frame = frame;
				string insn = dis.DisassembleInstruction (ref frame);
				entry.Text = String.Format ("0x{0:x}   {1}", old_frame.Address, insn);
				widget.Sensitive = true;
			} catch (Exception e) {
				Console.WriteLine (e);
				widget.Sensitive = false;
			}
		}

		void FrameChanged (StackFrame frame)
		{
			Update (frame.TargetAddress);
		}
		
		public void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.STOPPED:
				Update (backend.CurrentFrameAddress);
				break;

			default:
				widget.Sensitive = false;
				break;
			}
			MainIteration ();
		}
	}
}
