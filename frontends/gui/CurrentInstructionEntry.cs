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

		public CurrentInstructionEntry (DebuggerGUI gui, string glade_name)
			: this (gui, (Gtk.Container) gui.GXML [glade_name])
		{ }

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
			AssemblerLine line = CurrentFrame.DisassembleInstruction (address);
			if (line != null) {
				current_insn = line.FullText;
				stack.Push (address + line.InstructionSize);
				Update ();
			} else {
				current_insn = null;
			}
		}

		protected void Update ()
		{
			entry.Text = current_insn != null ? current_insn : "";
			widget.Sensitive = current_insn != null;
		}

		protected override void OnTargetEvent (TargetEventArgs args)
		{
			if (!IsVisible)
				return;

			if (!args.IsStopped) {
				current_insn = null;
			} else {
				TargetAddress addr = args.Frame.TargetAddress;
				AssemblerLine line = args.Frame.DisassembleInstruction (addr);
				if (line != null) {
					current_insn = line.FullText;

					stack = new Stack ();
					stack.Push (addr);
					stack.Push (addr + line.InstructionSize);
				} else {
					current_insn = null;
					stack = null;
				}
			}

			Update ();
		}
	}
}
