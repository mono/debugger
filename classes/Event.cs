using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public enum EventType
	{
		CatchException
	}

	[Serializable]
	public abstract class Event
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

		// <summary>
		//   This method is called each time the breakpoint is hit.
		//   It returns true if the target should remain stopped and false
		//   if the breakpoint is to be ignored.
		// </summary>
		// <remarks>
		//   The @target argument is *not* serializable and may not be used
		//   anywhere outside this handler.
		// </remarks>
		public abstract bool CheckBreakpointHit (Thread target, TargetAddress address);

		public abstract bool IsEnabled {
			get;
		}

		public abstract void Enable (Thread target);

		public abstract void Disable (Thread target);

		public abstract void Remove (Thread target);

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
			this.index = ++next_event_index;
			this.name = name;
			this.group = group;

			if (group == null)
				throw new NullReferenceException ();
		}
	}
}
