using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Languages.CSharp
{
	internal class CSharpMethod : MethodSource
	{
		int start_row, end_row;
		ArrayList addresses;
		MonoSymbolTableReader reader;
		JitLineNumberEntry[] line_numbers;
		MethodEntry method;
		SourceMethod source_method;
		IMethod imethod;
		SourceFileFactory factory;

		public CSharpMethod (MonoSymbolTableReader reader, IMethod imethod,
				     SourceMethod source_method, MethodEntry method,
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

		void generate_line_number (ArrayList lines, TargetAddress address, int offset)
		{
			for (int i = method.NumLineNumbers - 1; i >= 0; i--) {
				LineNumberEntry lne = method.LineNumbers [i];

				if (lne.Offset > offset)
					continue;

				lines.Add (new LineEntry (address, lne.Row));
				break;
			}
		}

		protected override MethodSourceData ReadSource ()
		{
			ArrayList lines = new ArrayList ();

			for (int i = 0; i < line_numbers.Length; i++) {
				JitLineNumberEntry lne = line_numbers [i];

				generate_line_number (lines, imethod.StartAddress + lne.Address, lne.Offset);
			}

			lines.Sort ();
			ISourceBuffer buffer = factory.FindFile (source_method.SourceFile.FileName);
			return new MethodSourceData (start_row, end_row, lines, source_method, buffer);
		}

		public override SourceMethod[] MethodLookup (string query)
		{
			string class_name = method.MethodBase.ReflectedType.FullName;
			string full_name = String.Concat (class_name, ".", query);
			return reader.MethodLookup (full_name);
		}
	}
}
