using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	[Serializable]
	public class SymbolTableException : TargetException
	{
		public SymbolTableException (string message, params object[] args)
			: base (TargetError.SymbolTable, String.Format (message, args))
		{ }


		protected SymbolTableException (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{
		}
	}
}
