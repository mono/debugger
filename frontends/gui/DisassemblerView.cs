using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : SourceView
	{
		public DisassemblerView (IDebuggerBackend backend, Gtk.Container container,
					 Gtk.TextView widget)
			: base (backend, container, widget)
		{ }

		protected override IMethodSource GetMethodSource (IMethod method)
		{
			if (method == null)
				return null;

			if ((backend.Inferior == null) || (backend.Inferior.Disassembler == null))
				return null;

			return backend.Inferior.Disassembler.DisassembleMethod (method);
		}

		protected override ISourceLocation GetSource (IStackFrame frame)
		{
			if (current_method_source == null)
				return null;

			return current_method_source.Lookup (frame.TargetAddress);
		}
	}
}
