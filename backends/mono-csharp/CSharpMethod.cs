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
		IMethod imethod;
		SourceFile file;

		public CSharpMethod (MonoSymbolTableReader reader, IMethod imethod,
				     SourceFile file, MethodEntry method,
				     JitLineNumberEntry[] line_numbers)
			: base (imethod, file)
		{
			this.reader = reader;
			this.imethod = imethod;
			this.method = method;
			this.file = file;
			this.line_numbers = line_numbers;
			this.start_row = method.StartRow;
			this.end_row = method.EndRow;
		}

		protected override MethodSourceData ReadSource ()
		{
			ArrayList lines = new ArrayList ();

			for (int i = 0; i < line_numbers.Length; i++) {
				JitLineNumberEntry lne = line_numbers [i];

				lines.Add (new LineEntry (imethod.StartAddress + lne.Address, lne.Line));
			}

			lines.Sort ();
			return new MethodSourceData (start_row, end_row, lines);
		}

		public override SourceMethod[] MethodLookup (string query)
		{
			string class_name = method.MethodBase.ReflectedType.FullName;
			string full_name = String.Concat (class_name, ".", query);

			return reader.MethodLookup (full_name);
		}
	}
}
