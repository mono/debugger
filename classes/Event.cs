using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public enum EventType
	{
		CatchException
	}

	public abstract class Event : DebuggerMarshalByRefObject
	{
		// <summary>
		//   An automatically generated unique index for this event.
		// </summary>
		public int Index {
			get { return index; }
		}

		// <summary>
		//   The event's name.  This property has no meaning at all for the
		//   backend, it's just something which can be displayed to the user to
		//   help him indentify this event.
		// </summary>
		public string Name {
			get { return name; }
		}

		// <summary>
		//   The ThreadGroup in which this breakpoint "breaks".
		//   If null, then it breaks in all threads.
		// </summary>
		public ThreadGroup ThreadGroup {
			get { return group; }
		}

		public bool Breaks (int id)
		{
			if (group.IsSystem)
				return true;

			foreach (int thread in group.Threads) {
				if (thread == id)
					return true;
			}

			return false;
		}

		public abstract bool IsEnabled {
			get;
		}

		public abstract void Enable (Thread target);

		public abstract void Disable (Thread target);

		public abstract void Remove (Thread target);

		//
		// Session handling.
		//

		internal virtual void GetSessionData (SerializationInfo info)
		{
			info.AddValue ("index", index);
			info.AddValue ("group", group);
			info.AddValue ("name", name);
		}

		internal virtual void SetSessionData (SerializationInfo info, ProcessServant process)
		{
			index = info.GetInt32 ("index");
			group = (ThreadGroup) info.GetValue ("group", typeof (ThreadGroup));
			name = info.GetString ("name");
		}

		protected internal class SessionSurrogate : ISerializationSurrogate
		{
			public virtual void GetObjectData (object obj, SerializationInfo info,
							   StreamingContext context)
			{
				Event handle = (Event) obj;
				handle.GetSessionData (info);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				Event handle = (Event) obj;
				ProcessServant process = (ProcessServant) context.Context;
				handle.SetSessionData (info, process);
				if ((context.State & StreamingContextStates.Persistence) != 0)
					handle.index = GetNextEventIndex ();
				return handle;
			}
		}

		//
		// Everything below is private.
		//

		int index;
		string name;
		ThreadGroup group;
		static int next_event_index = 0;

		internal static int GetNextEventIndex ()
		{
			return ++next_event_index;
		}

		protected Event (string name, ThreadGroup group)
		{
			this.index = GetNextEventIndex ();
			this.name = name;
			this.group = group;

			if (group == null)
				throw new NullReferenceException ();
		}
	}
}
