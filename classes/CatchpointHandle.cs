using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class CatchpointHandle : IEventHandle
	{
		Breakpoint breakpoint;
		int event_id;

		private CatchpointHandle (TargetAccess target, Breakpoint breakpoint)
		{
			this.breakpoint = breakpoint;

			Enable (target);
		}

		public static CatchpointHandle Create (TargetAccess target, Breakpoint breakpoint)
		{
			return new CatchpointHandle (target, breakpoint);
		}

#region IEventHandle
		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public bool IsEnabled {
			get { return (event_id > 0); }
		}

		public void Enable (TargetAccess target)
		{
			lock (this) {
				EnableCatchpoint (target);
			}
		}

		public void Disable (TargetAccess target)
		{
			lock (this) {
				DisableCatchpoint (target);
			}
		}

		public void Remove (TargetAccess target)
		{
			Disable (target);
		}
#endregion

		void EnableCatchpoint (TargetAccess target)
		{
			lock (this) {
				if (event_id > 0)
					return;

				event_id = target.AddEventHandler (EventType.CatchException, breakpoint);
			}
		}

		void DisableCatchpoint (TargetAccess target)
		{
			lock (this) {
				if (event_id > 0)
					target.RemoveEventHandler (event_id);

				event_id = -1;
			}
		}
	}
}
