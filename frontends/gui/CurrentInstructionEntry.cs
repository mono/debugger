using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class CurrentInstructionEntry : DebuggerWidget
	{
		string current_insn = null;
		DebuggerEntry entry;
		Stack stack = null;

		public CurrentInstructionEntry (DebuggerGUI gui, Gtk.Container container)
			: base (gui, null, container)
		{
			entry = new DebuggerEntry ();
			entry.Editable = false;
			entry.PreviousLine += new EventHandler (previous_line);
			entry.NextLine += new EventHandler (next_line);

			widget = entry;

			container.Add (entry);
			container.ShowAll ();
		}

		void previous_line (object o, EventArgs args)
		{
			lock (this) {
				if ((stack == null) || (CurrentFrame == null))
					return;

				if (stack.Count < 3)
					return;

				stack.Pop ();
				stack.Pop ();
				disassemble_line ((TargetAddress) stack.Peek ());
			}
		}

		void next_line (object o, EventArgs args)
		{
			lock (this) {
				if ((stack == null) || (CurrentFrame == null))
					return;

				disassemble_line ((TargetAddress) stack.Peek ());
			}
		}

		void disassemble_line (TargetAddress address)
		{
			try {
				TargetAddress old_addr = address;
				string insn = CurrentFrame.DisassembleInstruction (ref address);
				current_insn = String.Format ("0x{0:x}   {1}", old_addr.Address, insn);
				stack.Push (address);
				Update ();
			} catch {
				// Do nothing.
			}
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

					stack = new Stack ();
					stack.Push (old_addr);
					stack.Push (addr);
				} catch (Exception e) {
					Console.WriteLine (e);
					current_insn = null;
					stack = null;
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
