using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : DebuggerWidget
	{
		Gtk.TextView disassembler_view;
		Gtk.TextBuffer text_buffer;
		Gtk.TextTag frame_tag;
		Gtk.TextMark frame_mark;

		bool has_frame;

		public DisassemblerView (IDebuggerBackend backend, Gtk.TextView widget)
			: base (backend, widget)
		{
			disassembler_view = widget;

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text_buffer = disassembler_view.Buffer;
			text_buffer.TagTable.Add (frame_tag);

			frame_mark = text_buffer.CreateMark ("frame", text_buffer.StartIter, true);

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFramesInvalidHandler (FramesInvalidEvent);
		}

		MethodEntry current_method = null;
		Hashtable address_hash = null;

		void FramesInvalidEvent ()
		{
			has_frame = false;
			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
		}

		void FrameChangedEvent (IStackFrame frame)
		{
			has_frame = true;

			Gtk.TextBuffer buffer = disassembler_view.Buffer;

			if ((frame.SymbolHandle == null) || (backend.Inferior.Disassembler == null)) {
				text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);
				current_method = null;
				return;
			}

			IDisassembler dis = backend.Inferior.Disassembler;
			MethodEntry method = frame.SymbolHandle.Method;

			int row = 0;
			if (current_method != method) {
				current_method = method;

				text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

				long start = (long) method.Address.StartAddress;
				long end = (long) method.Address.EndAddress;

				ITargetLocation current = new TargetLocation (start);
				address_hash = new Hashtable ();

				while (current.Address < end) {
					long address = current.Address;
					string insn = dis.DisassembleInstruction (ref current);

					string line = String.Format ("{0:x}   {1}\n", address, insn);
			
					text_buffer.Insert (text_buffer.EndIter, line, line.Length);
					address_hash.Add (address, row++);
				}
			}

			row = (int) address_hash [frame.TargetLocation.Address];

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, row, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, row + 1, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			text_buffer.MoveMark (frame_mark, start_iter);

			disassembler_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
	}
}
