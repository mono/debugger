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

		public CurrentInstructionEntry (DebuggerGUI gui, Gtk.Entry widget)
			: base (gui, widget)
		{
			entry = widget;
			entry.Sensitive = false;
		}

		protected void Update ()
		{
			entry.Text = current_insn != null ? current_insn : "";
			widget.Sensitive = current_insn != null;
		}

		protected override void FrameChanged (StackFrame frame)
		{
			Update ();
		}

		protected override void FramesInvalid ()
		{
			Update ();
		}

		protected override void RealFrameChanged (StackFrame frame)
		{
			lock (this) {
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

			base.RealFrameChanged (frame);
		}

		protected override void RealFramesInvalid ()
		{
			lock (this) {
				current_insn = null;
			}
			base.RealFramesInvalid ();
		}
		
		protected override void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			if (new_state != TargetState.STOPPED)
				widget.Sensitive = false;
		}
	}
}
