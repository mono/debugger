using System;

namespace Mono.Debugger
{
	public class TargetInvocationException : TargetException
	{
		ITargetClassObject exception;

		public TargetInvocationException (ITargetClassObject exception)
			: base (String.Format ("Target invocation failed"))
		{
			this.exception = exception;
		}

		ITargetClassObject Exception {
			get {
				return exception;
			}
		}
	}
}
