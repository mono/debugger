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
		Hashtable namespaces;

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

			LineEntry[] addresses = new LineEntry [lines.Count];
			lines.CopyTo (addresses, 0);

			ISourceBuffer buffer = factory.FindFile (source_method.SourceFile.FileName);
			return new MethodSourceData (start_row, end_row, addresses, source_method, buffer);
		}

		public override string[] GetNamespaces ()
		{
			int index = method.NamespaceID;

			if (namespaces == null) {
				namespaces = new Hashtable ();

				C.SourceFileEntry source = method.SourceFile;
				foreach (C.NamespaceEntry entry in source.Namespaces)
					namespaces.Add (entry.Index, entry);
			}

			ArrayList list = new ArrayList ();

			while ((index > 0) && namespaces.Contains (index)) {
				C.NamespaceEntry ns = (C.NamespaceEntry) namespaces [index];
				list.Add (ns.Name);
				list.AddRange (ns.UsingClauses);

				index = ns.Parent;
			}

			string[] retval = new string [list.Count];
			list.CopyTo (retval, 0);
			return retval;
		}
	}
}
