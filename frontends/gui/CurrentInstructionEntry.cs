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
		}
		
		public void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			switch (new_state) {
			case TargetState.STOPPED:
				if (!backend.HasTarget || (backend.Disassembler == null)) {
					widget.Sensitive = false;
					break;
				}

				try {
					IDisassembler dis = backend.Disassembler;
					TargetAddress frame = backend.CurrentFrameAddress;
					string insn = dis.DisassembleInstruction (ref frame);
					entry.Text = String.Format ("0x{0:x}   {1}", frame.Address, insn);
					widget.Sensitive = true;
				} catch (Exception e) {
					Console.WriteLine (e);
					widget.Sensitive = false;
				}
				break;

			default:
				widget.Sensitive = false;
				break;
			}
			MainIteration ();
		}
	}
}
