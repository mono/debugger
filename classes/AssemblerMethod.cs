using System;
using System.Collections;

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
		SourceBuffer buffer;
		int start_row;
		int end_row;
		Module module;
		ArrayList addresses, lines;
		TargetAddress start_address, end_address;
		ArrayList contents;
		string name;

		public AssemblerMethod (Module module, TargetAddress start, TargetAddress end,
					string name, AssemblerLine[] lines)
			: base (start, start)
		{
			start_row = end_row = 0;
			addresses = null;

			this.module = module;
			this.name = name;
			this.start_address = start;
			this.end_address = start;

			this.lines = new ArrayList ();
			this.addresses = new ArrayList ();
			this.contents = new ArrayList ();

			foreach (AssemblerLine line in lines)
				add_one_line (line);
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
			contents = new ArrayList ();

			add_one_line (line);
		}

		void add_one_line (AssemblerLine line)
		{
			if (line.Label != null) {
				if (end_row > 0) {
					contents.Add ("");
					end_row++;
				} else
					start_row++;
				contents.Add (String.Format ("{0}:", line.Label));
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
			contents.Add (String.Format ("  {0:x}   {1}", line.Address, line.Text));
			SetEndAddress (line.Address + line.InstructionSize);
		}

		protected override void SetEndAddress (TargetAddress end)
		{
			this.end_address = end;
			base.SetEndAddress (end);
		}

		public override string Name {
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

		protected override MethodSourceData ReadSource ()
		{
			buffer = new SourceBuffer (name, contents);
			LineEntry[] lines = new LineEntry [addresses.Count];
			addresses.CopyTo (lines);
			return new MethodSourceData (start_row, end_row, lines, buffer, module);
		}
	}
}
