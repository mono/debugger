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

		IMethod current_method = null;
		IMethodSource current_method_source = null;

		void MethodChanged (StackFrame frame)
		{
			current_method_source = null;
			current_method = null;

			if ((frame.Method == null) || !frame.Method.IsLoaded)
				return;

			current_method = frame.Method;
			current_method_source = frame.DisassembleMethod ();

			if (current_method_source == null)
				text_buffer.Text = "";
			else
				text_buffer.Text = current_method_source.SourceBuffer.Contents;
		}

		protected override SourceLocation GetSourceLocation (StackFrame frame)
		{
			if (frame.Method != current_method)
				MethodChanged (frame);

			if (current_method_source == null)
				return null;

			return current_method_source.Lookup (frame.TargetAddress);
		}
	}
}
