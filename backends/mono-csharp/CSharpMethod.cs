using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	internal class CSharpMethod : MethodSource
	{
		int start_row, end_row;
		ISourceBuffer source;
		LineNumberEntry[] line_numbers;
		int[] line_addresses;
		MethodEntry method;
		IMethod imethod;

		private CSharpMethod (IMethod imethod, MethodEntry method, int[] line_addresses,
				      ISourceBuffer source, int start_row, int end_row,
				      LineNumberEntry[] line_numbers)
			: base (imethod)
		{
			this.imethod = imethod;
			this.method = method;
			this.line_addresses = line_addresses;
			this.source = source;
			this.start_row= start_row;
			this.end_row = end_row;
			this.line_numbers = line_numbers;
		}

		protected CSharpMethod (IMethod imethod, ISourceBuffer source,
					MethodEntry method, int[] line_addresses)
			: base (imethod)
		{
			this.imethod = imethod;
			this.source = source;
			this.method = method;
			this.line_addresses = line_addresses;
			this.start_row = method.StartRow;
			this.end_row = method.EndRow;
			this.line_numbers = method.LineNumbers;
		}

		public static CSharpMethod GetMethodSource (IMethod imethod, MethodEntry method,
							    int[] line_addresses, SourceFileFactory factory)
		{
			if (method.SourceFile != null) {
				ISourceBuffer buffer = factory.FindFile (method.SourceFile);

				if (buffer != null)
					return new CSharpMethod (imethod, buffer, method, line_addresses);
			}

			if (method.Token >> 24 != 6)
				throw new InvalidOperationException ();

			int index = (int) (method.Token & 0xffffff);
			ILDisassembler dis = ILDisassembler.Disassemble (imethod.ImageFile);

			return new CSharpMethod (imethod, method, line_addresses, dis,
						 dis.GetStartLine (index), dis.GetEndLine (index),
						 dis.GetLines (index));
		}

		internal static ISourceBuffer GetMethodSource (IMethod imethod, MethodEntry method,
							       int[] line_addresses,
							       SourceFileFactory factory,
							       out int start_row, out int end_row,
							       out ArrayList addresses)
		{
			CSharpMethod csharp = GetMethodSource (imethod, method, line_addresses, factory);
			if (csharp == null) {
				start_row = end_row = 0;
				addresses = null;
				return null;
			}

			return csharp.ReadSource (out start_row, out end_row, out addresses);
		}

		protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses)
		{
			start_row = this.start_row;
			end_row = this.end_row;
			addresses = get_lines ();
			return source;
		}

		ArrayList get_lines ()
		{
			ArrayList lines = new ArrayList ();

			for (int i = 0; i < line_numbers.Length; i++) {
				LineNumberEntry lne = line_numbers [i];
				int line_address = line_addresses [i];

				lines.Add (new LineEntry (imethod.StartAddress + line_address, lne.Row));
			}

			lines.Sort ();
			return lines;
		}
	}
}
