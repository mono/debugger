using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpMethod : CSharpMethodBase
	{
		static CSharpMethodSource get_method_source (CSharpSymbolTable symtab, MethodEntry method)
		{
			if (method.SourceFile == null)
				return GetILSource (symtab, method);
			else {
				ISourceBuffer buffer = symtab.SourceFactory.FindFile (method.SourceFile);
				if (buffer != null)
					return new CSharpMethodSource (
						buffer, method, (int) method.StartRow, (int) method.EndRow,
						method.LineNumbers);
			}

			return null;
		}

		public CSharpMethod (CSharpSymbolTable symtab, MethodEntry method)
			: base (symtab, method, get_method_source (symtab, method))
		{ }
	}
}
