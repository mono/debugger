using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
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
		ISourceBuffer buffer;
		int start_row;
		int end_row;
		ArrayList addresses, lines;
		TargetAddress start_address, end_address;
		StringBuilder sb;
		string name;

		public AssemblerMethod (TargetAddress start, TargetAddress end, string name,
					IDisassembler disassembler)
			: base (start, start)
		{
			start_row = end_row = 0;
			addresses = null;

			this.name = name;
			this.start_address = start;
			this.end_address = start;

			lines = new ArrayList ();
			addresses = new ArrayList ();
			sb = new StringBuilder ();

			TargetAddress current = start_address;

			while (current < end) {
				AssemblerLine line = disassembler.DisassembleInstruction (current);
				if (line == null)
					break;

				current += line.InstructionSize;
				add_one_line (line);
			}
		}

		public AssemblerMethod (AssemblerLine line)
			: base (line.Address, line.Address)
		{
			start_row = end_row = 0;
			addresses = null;

			this.name = line.Address.ToString ();
			this.start_address = line.Address;
			this.end_address = line.Address;

			lines = new ArrayList ();
			addresses = new ArrayList ();
			sb = new StringBuilder ();

			add_one_line (line);
		}

		void add_one_line (AssemblerLine line)
		{
			if (line.Label != null) {
				if (end_row > 0) {
					sb.Append ("\n");
					end_row++;
				} else
					start_row++;
				sb.Append (String.Format ("{0}:\n", line.Label));
				end_row++;
			}

			AppendOneLine (line);
		}

		public void AppendOneLine (AssemblerLine line)
		{
			if (line.Address != EndAddress)
				throw new ArgumentException (String.Format (
					"Requested to add instruction at address {0}, but " +
					"method ends at {1}.", line.Address, EndAddress));

			lines.Add (line);
			addresses.Add (new LineEntry (line.Address, ++end_row));
			sb.Append (String.Format ("  {0:x}   {1}\n", line.Address, line.Text));
			SetEndAddress (line.Address + line.InstructionSize);
		}

		protected override void SetEndAddress (TargetAddress end)
		{
			this.end_address = end;
			base.SetEndAddress (end);
		}

		public string Name {
			get { return name; }
		}

		public TargetAddress StartAddress {
			get { return start_address; }
		}

		public TargetAddress EndAddress {
			get { return end_address; }
		}

		public AssemblerLine[] Lines {
			get {
				AssemblerLine[] retval = new AssemblerLine [lines.Count];
				lines.CopyTo (retval, 0);
				return retval;
			}
		}

		protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses)
		{
			start_row = this.start_row;
			end_row = this.end_row;
			addresses = this.addresses;
			return new SourceBuffer (name, sb.ToString ());
		}

		public override SourceMethod[] MethodLookup (string query)
		{
			return new SourceMethod [0];
		}
	}
}
