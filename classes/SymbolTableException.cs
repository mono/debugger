using System;

namespace Mono.Debugger
{
	public class SymbolTableException : TargetException
	{
		public SymbolTableException (string message, params object[] args)
			: base (TargetError.SymbolTable, String.Format (message, args))
		{ }
	}
}
