using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public enum EventType
	{
		CatchException
	}

	public class EventHandle
	{
		EventType type;
		Breakpoint breakpoint;
		int event_id;

		private EventHandle (Process process, EventType type, Breakpoint breakpoint)
		{
			this.type = type;
			this.breakpoint = breakpoint;

			EnableEvent (process);
		}

		public static EventHandle Create (Process process, EventType type, Breakpoint breakpoint)
		{
			return new EventHandle (process, type, breakpoint);
		}

		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public bool IsEnabled {
			get { return (event_id > 0); }
		}

		protected void Enable (Process process)
		{
			lock (this) {
				if (event_id > 0)
					return;

				event_id = process.AddEventHandler (type, breakpoint);
			}
		}

		protected void Disable (Process process)
		{
			lock (this) {
				if (event_id > 0)
					process.RemoveEventHandler (event_id);

				event_id = -1;
			}
		}

		public void EnableEvent (Process process)
		{
			lock (this) {
				Enable (process);
			}
		}

		public void DisableEvent (Process process)
		{
			lock (this) {
				Disable (process);
			}
		}

		public void RemoveEvent (Process process)
		{
			DisableEvent (process);
		}
	}
}
