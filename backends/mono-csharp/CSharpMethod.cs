using System;
using System.Collections;
using C = Mono.CSharp.Debugger;

namespace Mono.Debugger.Languages.CSharp
{
	internal class CSharpMethod : MethodSource
	{
		int start_row, end_row;
		MonoSymbolFile reader;
		JitLineNumberEntry[] line_numbers;
		C.MethodEntry method;
		SourceMethod source_method;
		IMethod imethod;
		SourceFileFactory factory;

		public CSharpMethod (MonoSymbolFile reader, IMethod imethod,
				     SourceMethod source_method, C.MethodEntry method,
				     JitLineNumberEntry[] line_numbers)
			: base (imethod, source_method.SourceFile)
		{
			this.reader = reader;
			this.imethod = imethod;
			this.method = method;
			this.source_method = source_method;
			this.line_numbers = line_numbers;
			this.start_row = method.StartRow;
			this.end_row = method.EndRow;
			this.factory = reader.Table.Backend.SourceFileFactory;
		}

		void generate_line_number (ArrayList lines, TargetAddress address, int offset,
					   ref int last_line)
		{
			for (int i = method.NumLineNumbers - 1; i >= 0; i--) {
				C.LineNumberEntry lne = method.LineNumbers [i];

				if (lne.Offset > offset)
					continue;

				if (lne.Row > last_line) {
					lines.Add (new LineEntry (address, lne.Row));
					last_line = lne.Row;
				}

				break;
			}
		}

		protected override MethodSourceData ReadSource ()
		{
			ArrayList lines = new ArrayList ();
			int last_line = -1;

			for (int i = 0; i < line_numbers.Length; i++) {
				JitLineNumberEntry lne = line_numbers [i];

				generate_line_number (lines, imethod.StartAddress + lne.Address,
						      lne.Offset, ref last_line);
			}

			lines.Sort ();

			ISourceBuffer buffer = factory.FindFile (source_method.SourceFile.FileName);
			return new MethodSourceData (start_row, end_row, lines, source_method, buffer);
		}
	}
}
