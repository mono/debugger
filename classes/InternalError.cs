using System;

namespace Mono.Debugger
{
	public class InternalError : Exception
	{
		public InternalError ()
			: base ("Internal error.")
		{ }

		public InternalError (string message, params object[] args)
			: base (String.Format (message, args))
		{ }

		public InternalError (Exception inner, string message, params object[] args)
			: base (String.Format (message, args), inner)
		{ }
	}
}
