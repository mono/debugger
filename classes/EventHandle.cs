using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public enum EventType
	{
		CatchException
	}

	[Serializable]
	public class EventHandle : ISerializable, IDeserializationCallback
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

		//
		// ISerializable
		//

		private Process internal_process;

		public virtual void GetObjectData (SerializationInfo info,
						   StreamingContext context)
		{
			info.AddValue ("type", type);
			info.AddValue ("breakpoint", breakpoint);
			info.AddValue ("enabled", IsEnabled);
		}

		protected EventHandle (SerializationInfo info, StreamingContext context)
		{
			type = (EventType) info.GetValue ("type", typeof (EventType));
			breakpoint = (Breakpoint) info.GetValue ("breakpoint", typeof (Breakpoint));
			if (info.GetBoolean ("enabled"))
				internal_process = (Process) context.Context;
		}

		void IDeserializationCallback.OnDeserialization (object sender)
		{
			if (internal_process == null)
				return;

			EnableEvent (internal_process);
			internal_process = null;
		}
	}
}
