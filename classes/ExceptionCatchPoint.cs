using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public class ExceptionCatchPoint : Breakpoint
	{
		public ExceptionCatchPoint (ILanguage language, ITargetType exception, ThreadGroup group)
			: base (exception.Name, group, true)
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

		public override bool CheckBreakpointHit (TargetAddress exc_address, StackFrame frame,
							 ITargetAccess target)
		{
			ITargetClassObject exc = language.CreateObject (frame, exc_address) as ITargetClassObject;
			if (exc == null)
				return false; // OOOPS

			return IsSubclassOf (exc.Type, exception);
		}

		public override void BreakpointHit (StackFrame frame)
		{
			OnBreakpointHit (frame);
		}

		public event BreakpointEventHandler BreakpointHitEvent;

		protected virtual void OnBreakpointHit (StackFrame frame)
		{
			if (BreakpointHitEvent != null)
				BreakpointHitEvent (this, frame);
		}
	}
}
