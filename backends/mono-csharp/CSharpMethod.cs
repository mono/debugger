using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Languages.CSharp
{
	internal class CSharpMethod : MethodSource
	{
		int start_row, end_row;
		ISourceBuffer source;
		JitLineNumberEntry[] line_numbers;
		MethodEntry method;
		IMethod imethod;

		protected CSharpMethod (IMethod imethod, ISourceBuffer source,
					MethodEntry method, JitLineNumberEntry[] line_numbers)
			: base (imethod)
		{
			this.imethod = imethod;
			this.source = source;
			this.method = method;
			this.line_numbers = line_numbers;
			this.start_row = method.StartRow;
			this.end_row = method.EndRow;
		}

		public static CSharpMethod GetMethodSource (IMethod imethod, MethodEntry method,
							    JitLineNumberEntry[] line_numbers)
		{
			if (method.SourceFile == null)
				return null;

			ISourceBuffer buffer = null;
			if (buffer == null)
				buffer = new SourceBuffer (method.SourceFile.FileName);

			return new CSharpMethod (imethod, buffer, method, line_numbers);
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
				JitLineNumberEntry lne = line_numbers [i];

				lines.Add (new LineEntry (imethod.StartAddress + lne.Address, lne.Line));
			}

			lines.Sort ();
			return lines;
		}
	}
}
