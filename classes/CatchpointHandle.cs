using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class CatchpointHandle : IEventHandle
	{
		Breakpoint breakpoint;
		int event_id;

		private CatchpointHandle (Process process, Breakpoint breakpoint)
		{
			this.breakpoint = breakpoint;

			Enable (process);
		}

		public static CatchpointHandle Create (Process process, Breakpoint breakpoint)
		{
			return new CatchpointHandle (process, breakpoint);
		}

#region IEventHandle
		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public bool IsEnabled {
			get { return (event_id > 0); }
		}

		public void Enable (Process process)
		{
			lock (this) {
				EnableCatchpoint (process);
			}
		}

		public void Disable (Process process)
		{
			lock (this) {
				DisableCatchpoint (process);
			}
		}

		public void Remove (Process process)
		{
			Disable (process);
		}
#endregion

		void EnableCatchpoint (Process process)
		{
			lock (this) {
				if (event_id > 0)
					return;

				event_id = process.AddEventHandler (EventType.CatchException, breakpoint);
			}
		}

		void DisableCatchpoint (Process process)
		{
			lock (this) {
				if (event_id > 0)
					process.RemoveEventHandler (event_id);

				event_id = -1;
			}
		}
	}
}
