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

		IMethodSource current_method = null;

		protected override ISourceLocation GetSource (IStackFrame frame)
		{
			if (current_method != null) {
				ISourceLocation source = current_method.Lookup (frame.TargetAddress);
				if (source != null)
					return source;
			}

			if ((backend.Inferior == null) || (backend.Inferior.Disassembler == null))
				return null;

			if (frame.Method == null)
				return null;

			current_method = backend.Inferior.Disassembler.DisassembleMethod (frame.Method);

			return current_method.Lookup (frame.TargetAddress);
		}
	}
}
