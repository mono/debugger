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

		protected override Gtk.Widget CreateWidget (Gtk.SourceView source_view)
		{
			return source_view;
		}

		public DisassemblerView (SourceManager manager, RegisterDisplay register_display)
			: base (manager)
		{
			this.register_display = register_display;

			manager.MethodInvalidEvent += new MethodInvalidHandler (MethodInvalid);
		}

		IMethod current_method = null;
		AssemblerMethod current_method_source = null;
		ArrayList dynamic_methods = null;

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
			dynamic_methods = null;

			if ((frame.Method != null) && frame.Method.IsLoaded) {
				current_method = frame.Method;
				current_method_source = frame.DisassembleMethod ();
			}

			if (current_method_source == null)
				text_buffer.Text = "";
			else {
				string[] lines = current_method_source.SourceBuffer.Contents;
				text_buffer.Text = String.Join ("\n", lines) + "\n";
			}
		}

		void MethodInvalid ()
		{
			// text_buffer.Text = "";
		}

		protected override SourceAddress GetSourceAddress (StackFrame frame)
		{
			if (frame.Method == null)
				UpdateDynamicMethod (frame);
			else if (frame.Method != current_method)
				MethodChanged (frame);

			if (dynamic_methods != null)
				return LookupDynamic (frame.TargetAddress);
			else if (current_method_source == null)
				return null;

			return current_method_source.Lookup (frame.TargetAddress);
		}

		protected sealed class DynamicMethod
		{
			public readonly AssemblerMethod Method;
			public int StartLine;
			public int NumLines;

			public DynamicMethod (AssemblerMethod method, int line)
			{
				this.Method = method;
				this.StartLine = line;

				NumLines = method.SourceBuffer.Contents.Length;
			}
		}

		void UpdateDynamicMethod (StackFrame frame)
		{
			if (dynamic_methods == null) {
				dynamic_methods = new ArrayList ();
				text_buffer.Text = "";
			}

			int index;
			int line = 0;
			int new_lines = 0;
			TextIter iter;

			for (index = dynamic_methods.Count-1; index >= 0; index--) {
				DynamicMethod dynamic = (DynamicMethod) dynamic_methods [index];

				if (frame.TargetAddress < dynamic.Method.StartAddress)
					continue;
				else if ((frame.TargetAddress >= dynamic.Method.StartAddress) &&
					 (frame.TargetAddress < dynamic.Method.EndAddress))
					return;
				else if (dynamic.Method.EndAddress == frame.TargetAddress) {
					TargetAddress address = frame.TargetAddress;
					AssemblerLine asm_line = frame.DisassembleInstruction (address);
					if (asm_line == null)
						return;

					dynamic.Method.AppendOneLine (asm_line);

					line = dynamic.StartLine + (dynamic.NumLines++);
					text_buffer.GetIterAtLineOffset (out iter, line, 0);
					source_view.ScrollToIter (iter, 0.0, true, 0.0, 0.5);

					string contents = String.Format (
						"  {0:x}   {1}\n", frame.TargetAddress, asm_line.Text);

					text_buffer.Insert (iter, contents);
					new_lines = 1;
					break;
				}

				line = dynamic.StartLine + dynamic.NumLines;
				break;
			}

			if (new_lines == 0) {
				AssemblerLine asm_line = frame.DisassembleInstruction (frame.TargetAddress);
				if (asm_line == null)
					return;

				AssemblerMethod method = new AssemblerMethod (asm_line);

				text_buffer.GetIterAtLineOffset (out iter, line, 0);
				source_view.ScrollToIter (iter, 0.0, true, 0.0, 0.5);

				string contents = "\n" + method.SourceBuffer.Contents;
				text_buffer.Insert (iter, contents);

				index++;
				DynamicMethod new_dynamic = new DynamicMethod (method, line + 1);
				dynamic_methods.Insert (index, new_dynamic);
				new_lines = new_dynamic.NumLines + 1;
			}

			for (++index; index < dynamic_methods.Count; index++) {
				DynamicMethod dynamic = (DynamicMethod) dynamic_methods [index];

				dynamic.StartLine += new_lines;
			}
		}

		SourceAddress LookupDynamic (TargetAddress address)
		{
			foreach (DynamicMethod dynamic in dynamic_methods) {
				if ((address < dynamic.Method.StartAddress) ||
				    (address >= dynamic.Method.EndAddress))
					continue;

				SourceAddress source = dynamic.Method.Lookup (address);
				if (source == null)
					return null;

				return new SourceAddress (
					source.MethodSource, source.Row + dynamic.StartLine,
					source.SourceOffset, source.SourceRange);
			}

			return null;
		}

		public override void InsertBreakpoint ()
		{
			Console.WriteLine ("INFO: Implement me, insert a breakpoint here");
		}
	}
}
