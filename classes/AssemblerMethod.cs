using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public struct AssemblerLine
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

			while (current < end)
				add_one_line (disassembler, ref current);
		}

		public AssemblerMethod (TargetAddress start, IDisassembler disassembler)
			: base (start, start)
		{
			start_row = end_row = 0;
			addresses = null;

			this.name = start.ToString ();
			this.start_address = start;
			this.end_address = start;

			lines = new ArrayList ();
			addresses = new ArrayList ();
			sb = new StringBuilder ();

			TargetAddress current = start;
			add_one_line (disassembler, ref current);
		}

		void add_one_line (IDisassembler disassembler, ref TargetAddress current)
		{
			string label = null;
			if (disassembler.SymbolTable != null) {
				IMethod imethod = disassembler.SymbolTable.Lookup (current);
				if ((imethod != null) && (imethod.StartAddress == current)) {
					label = imethod.Name;
					if (end_row > 0) {
						sb.Append ("\n");
						end_row++;
					} else
						start_row++;
					sb.Append (String.Format ("{0}:\n", label));
					end_row++;
				}
			}

			TargetAddress address = current;
			string insn = disassembler.DisassembleInstruction (ref current);
			byte insn_size = (byte) (current - address);

			AppendOneLine (new AssemblerLine (address, insn_size, insn));
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
	}
}
