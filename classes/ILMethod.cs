using System;
using System.Text;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class ILMethod : CSharpMethodBase
	{
		public ILMethod (CSharpSymbolTable symtab, MethodEntry method)
			: base (symtab, method, GetILSource (symtab, method))
		{ }
	}
}
