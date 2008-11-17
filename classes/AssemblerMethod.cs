using System;
using System.Collections;

using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public sealed class AssemblerLine
	{
		public readonly string Label;
		public readonly TargetAddress Address;
		public readonly byte InstructionSize;
		public readonly string Text;

		public AssemblerLine (string label, TargetAddress address, byte size, string text)
		{
			this.Label = label;
			this.Address = address;
			this.InstructionSize = size;
			this.Text = text;
		}

		public AssemblerLine (TargetAddress address, byte size, string text)
			: this (null, address, size, text)
		{ }

		public string FullText {
			get {
				return String.Format ("{0:x}   {1}", Address, Text);
			}
		}
	}

	public sealed class AssemblerMethod : MethodSource
	{
		readonly SourceBuffer buffer;
		readonly int start_row, end_row;
		readonly AssemblerLine[] lines;
		readonly Method method;
		ArrayList addresses;

		public AssemblerMethod (Method method, AssemblerLine[] lines)
		{
			this.method = method;
			this.lines = lines;
			addresses = new ArrayList ();

			ArrayList contents = new ArrayList ();
			foreach (AssemblerLine line in lines) {
				if (line.Label != null) {
					if (end_row > 0) {
						contents.Add ("");
						end_row++;
					} else
						start_row++;
					contents.Add (String.Format ("{0}:", line.Label));
					end_row++;
				}

				addresses.Add (new LineEntry (line.Address, 0, ++end_row));
				contents.Add (String.Format ("  {0:x}   {1}", line.Address, line.Text));
			}

			string[] text = new string [contents.Count];
			contents.CopyTo (text);

			buffer = new SourceBuffer (method.Name, text);
		}

		public override Module Module {
			get { return method.Module; }
		}

		public override string Name {
			get { return method.Name; }
		}

		public override bool IsManaged {
			get { return false; }
		}

		public override bool IsDynamic {
			get { return true; }
		}

		public override TargetClassType DeclaringType {
			get { throw new InvalidOperationException (); }
		}

		public override TargetFunctionType Function {
			get { throw new InvalidOperationException (); }
		}

		public override bool HasSourceFile {
			get { throw new InvalidOperationException (); }
		}

		public override SourceFile SourceFile {
			get { throw new InvalidOperationException (); }
		}

		public override bool HasSourceBuffer {
			get { return true; }
		}

		public override SourceBuffer SourceBuffer {
			get { return buffer; }
		}

		public override int StartRow {
			get { return start_row; }
		}

		public override int EndRow {
			get { return end_row; }
		}

		public override Method NativeMethod {
			get { return method; }
		}

		public AssemblerLine[] Lines {
			get { return lines; }
		}
	}
}
