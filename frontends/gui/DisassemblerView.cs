using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : SourceView
	{
		public DisassemblerView (SourceManager manager, Gtk.Container container)
			: base (manager, container)
		{ }

		IMethodSource current_method_source = null;
		bool dirty = false;

		public void RealMethodChanged (IMethod method)
		{
			RealMethodInvalid ();

			if (method == null)
				return;

			if (!process.HasTarget || (process.Disassembler == null))
				return;

			current_method_source = process.Disassembler.DisassembleMethod (method);
		}

		public void RealMethodInvalid ()
		{
			current_method_source = null;
			dirty = true;
		}

		void update_buffer ()
		{
			if (!dirty)
				return;

			if (current_method_source == null)
				text_buffer.Text = "";
			else
				text_buffer.Text = current_method_source.SourceBuffer.Contents;

			dirty = false;
		}

		protected override SourceLocation GetSourceLocation (StackFrame frame)
		{
			if (current_method_source == null)
				return null;

			update_buffer ();

			return current_method_source.Lookup (frame.TargetAddress);
		}
	}
}
