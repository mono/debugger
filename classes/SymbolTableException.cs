using System;

namespace Mono.Debugger
{
	public class SymbolTableException : Exception
	{
		public SymbolTableException ()
			: base ()
		{ }

		public SymbolTableException (string message, params object[] args)
			: base (String.Format (message, args))
		{ }
	}
}
