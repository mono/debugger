using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : SourceView
	{
		public DisassemblerView (IDebuggerBackend backend, Gtk.TextView widget)
			: base (backend, widget)
		{
		}

		IMethod current_method = null;

		protected override ISourceLocation GetSource (IStackFrame frame)
		{
			if (current_method != null) {
				ISourceLocation source = current_method.Lookup (frame.TargetLocation);
				if (source != null)
					return source;
			}

			if ((backend.Inferior == null) || (backend.Inferior.Disassembler == null))
				return null;

			if (frame.Method == null)
				return null;

			current_method = new NativeMethod (backend.Inferior.Disassembler, frame.Method);

			return current_method.Lookup (frame.TargetLocation);
		}
	}
}
