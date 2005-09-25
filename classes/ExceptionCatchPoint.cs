using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public class ExceptionCatchPoint : Breakpoint
	{
		public ExceptionCatchPoint (ILanguage language, TargetType exception, ThreadGroup group)
			: base (exception.Name, group)
		{
			this.language = language;
			this.exception = exception;
		}

		ILanguage language;
		TargetType exception;

		bool IsSubclassOf (TargetClassType type, TargetType parent)
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
			TargetClassObject exc = language.CreateObject (target, address)
				as TargetClassObject;
			if (exc == null)
				return false; // OOOPS

			return IsSubclassOf (exc.Type, exception);
		}
	}
}
