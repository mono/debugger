using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public abstract class CSharpMethodBase : MethodBase
	{
		protected CSharpSymbolTable symtab;
		protected MethodEntry method;
		protected CSharpMethodSource source;

		protected class CSharpMethodSource : IMethodSource
		{
			int start_row, end_row;
			ISourceBuffer source;
			LineNumberEntry[] LineNumbers;
			MethodEntry method;

			public CSharpMethodSource (ISourceBuffer source, MethodEntry method,
						   int start_row, int end_row,
						   LineNumberEntry[] LineNumbers)
			{
				this.source = source;
				this.method = method;
				this.start_row = start_row;
				this.end_row = end_row;
				this.LineNumbers = LineNumbers;
			}

			ArrayList get_lines ()
			{
				ArrayList lines = new ArrayList ();

				for (int i = 0; i < LineNumbers.Length; i++) {
					LineNumberEntry lne = LineNumbers [i];
					int line_address = (int) method.Address.LineAddresses [i];

					long address = (long) method.Address.StartAddress + line_address;

					lines.Add (new LineEntry (address, (int) lne.Row));
				}

				lines.Sort ();
				return lines;
			}

			public ISourceBuffer SourceBuffer {
				get {
					return source;
				}
			}

			public int StartRow {
				get {
					return start_row;
				}
			}

			public int EndRow {
				get {
					return end_row;
				}
			}

			public ArrayList Lines {
				get {
					return get_lines ();
				}
			}
		}

		protected CSharpMethodBase (CSharpSymbolTable symtab, MethodEntry method,
					    CSharpMethodSource source)
			: base (String.Format ("C#({0:x})", method.Token), symtab.ImageFile)
		{
			this.symtab = symtab;
			this.method = method;
			this.source = source;

			if (method.Address != null)
				SetAddresses ((long) method.Address.StartAddress,
					      (long) method.Address.EndAddress);
		}
		
		protected static CSharpMethodSource GetILSource (CSharpSymbolTable symtab, MethodEntry method)
		{
			if (method.Token >> 24 != 6)
				return null;

			int index = (int) (method.Token & 0xffffff);
			ILDisassembler dis = ILDisassembler.Disassemble (symtab.ImageFile);

			return new CSharpMethodSource (
				dis, method, dis.GetStartLine (index), dis.GetEndLine (index),
				dis.GetLines (index));
		}

		protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses)
		{
			start_row = source.StartRow;
			end_row = source.EndRow;
			addresses = source.Lines;
			return source.SourceBuffer;
		}

		//
		// IMethod
		//

		public override object MethodHandle {
			get {
				return (long) method.Token;
			}
		}
	}
}
