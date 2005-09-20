using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public class ExceptionCatchPoint : Breakpoint
	{
		public ExceptionCatchPoint (ILanguage language, ITargetType exception, ThreadGroup group)
			: base (exception.Name, group)
		{
			this.language = language;
			this.exception = exception;
		}

		ILanguage language;
		ITargetType exception;

		bool IsSubclassOf (ITargetClassType type, ITargetType parent)
		{
			while (type != null) {
				if (type == parent)
					return true;

				if (!type.HasParent)
					return false;

				type = type.ParentType;
			}

			return false;
		}

		public override bool CheckBreakpointHit (ITargetAccess target, TargetAddress address)
		{
			ITargetClassObject exc = language.CreateObject (target, address)
				as ITargetClassObject;
			if (exc == null)
				return false; // OOOPS

			return IsSubclassOf (exc.Type, exception);
		}
	}
}
