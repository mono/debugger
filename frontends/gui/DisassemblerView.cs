using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : SourceView
	{
		public DisassemblerView (DebuggerGUI gui, Gtk.Container container, Gtk.TextView widget)
			: base (gui, container, widget)
		{ }

		protected override IMethodSource GetMethodSource (IMethod method)
		{
			if (method == null)
				return null;

			if (!process.HasTarget || (process.Disassembler == null))
				return null;

			return process.Disassembler.DisassembleMethod (method);
		}

		protected override SourceLocation GetSource (StackFrame frame)
		{
			if (current_method_source == null)
				return null;

			return current_method_source.Lookup (frame.TargetAddress);
		}
	}
}
