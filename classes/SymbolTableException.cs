using System;

namespace Mono.Debugger
{
	[Serializable]
	public class SymbolTableException : TargetException
	{
		public SymbolTableException (string message, params object[] args)
			: base (TargetError.SymbolTable, String.Format (message, args))
		{ }
	}
}
