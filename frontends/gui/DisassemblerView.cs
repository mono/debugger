using Gtk;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class DisassemblerView : SourceView
	{
		RegisterDisplay register_display;

		public DisassemblerView (SourceManager manager, Gtk.Container container,
					 RegisterDisplay register_display)
			: base (manager, container)
		{
			this.register_display = register_display;

			manager.MethodInvalidEvent += new MethodInvalidHandler (MethodInvalid);
		}

		IMethod current_method = null;
		IMethodSource current_method_source = null;

		protected override void SetActive ()
		{
			base.SetActive ();
			register_display.Active = true;
		}

		protected override void SetInactive ()
		{
			base.SetInactive ();

			current_method = null;
			current_method_source = null;
			register_display.Active = false;
		}

		void MethodChanged (StackFrame frame)
		{
			current_method_source = null;
			current_method = null;

			if ((frame.Method == null) || !frame.Method.IsLoaded) {
				text_buffer.Text = "";
				return;
			}

			current_method = frame.Method;
			current_method_source = frame.DisassembleMethod ();

			if (current_method_source == null)
				text_buffer.Text = "";
			else
				text_buffer.Text = current_method_source.SourceBuffer.Contents;
		}

		void MethodInvalid ()
		{
			text_buffer.Text = "";
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
