using System;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public abstract class EventHandle : MarshalByRefObject, IEventHandle
	{
		protected readonly Breakpoint breakpoint;

		protected EventHandle (Breakpoint breakpoint)
		{
			this.breakpoint = breakpoint;
		}

		public static EventHandle InsertBreakpoint (TargetAccess target, Breakpoint bpt,
							    SourceLocation location)
		{
			return new BreakpointHandle (target, bpt, location);
		}

		public static EventHandle InsertBreakpoint (TargetAccess target, Breakpoint bpt,
							    TargetFunctionType func)
		{
			return new BreakpointHandle (target, bpt, func);
		}

#region IEventHandle
		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public abstract bool IsEnabled {
			get;
		}

		public abstract void Enable (TargetAccess target);

		public abstract void Disable (TargetAccess target);

		public abstract void Remove (TargetAccess target);
#endregion
	}
}
