using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public struct AssemblerLine
	{
		public readonly string Label;
		public readonly TargetAddress Address;
		public readonly string Text;

		public AssemblerLine (string label, TargetAddress address, string text)
		{
			this.Label = label;
			this.Address = address;
			this.Text = text;
		}

		public AssemblerLine (TargetAddress address, string text)
			: this (null, address, text)
		{ }
	}

	public sealed class AssemblerMethod : MethodSource
	{
		ISourceBuffer buffer;
		int start_row;
		int end_row;
		ArrayList addresses, lines;
		TargetAddress start_address, end_address;
		string name;

		public AssemblerMethod (TargetAddress start, TargetAddress end, string name,
					IDisassembler disassembler)
			: base (start, end)
		{
			start_row = end_row = 0;
			addresses = null;

			this.name = name;
			this.start_address = start;
			this.end_address = end;

			lines = new ArrayList ();
			addresses = new ArrayList ();
			StringBuilder sb = new StringBuilder ();

			TargetAddress current = start_address;

			while (current < end_address)
				add_one_line (disassembler, sb, ref current);

			buffer = new SourceBuffer (name, sb.ToString ());
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
			StringBuilder sb = new StringBuilder ();

			add_one_line (disassembler, sb, ref end_address);
			SetEndAddress (end_address);

			buffer = new SourceBuffer (name, sb.ToString ());
		}

		void add_one_line (IDisassembler disassembler, StringBuilder sb, ref TargetAddress current)
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
			string line = String.Format ("  {0:x}   {1}\n", address, insn);

			lines.Add (new AssemblerLine (label, address, insn));
			addresses.Add (new LineEntry (address, ++end_row));
			sb.Append (line);
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
			return this.buffer;
		}
	}
}
