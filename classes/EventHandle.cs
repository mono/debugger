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

		public static EventHandle InsertBreakpoint (Process process, Breakpoint bpt,
							    SourceLocation location)
		{
			return new BreakpointHandle (process, bpt, location);
		}

		public static EventHandle InsertBreakpoint (Process process, Breakpoint bpt,
							    ITargetFunctionType func)
		{
			return new BreakpointHandle (process, bpt, func);
		}

#region IEventHandle
		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public abstract bool IsEnabled {
			get;
		}

		public abstract void Enable (Process process);

		public abstract void Disable (Process process);

		public abstract void Remove (Process process);
#endregion
	}
}
