using System;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;
using System.Data;

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

		internal abstract void OnTargetExited ();

		//
		// Session handling.
		//

		protected virtual void GetSessionData (DataRow row)
		{
			row ["index"] = index;
			row ["name"] = name;
			row ["group"] = group.Name;
			row ["type"] = GetType ();
			row ["enabled"] = IsEnabled;
		}

		internal abstract void GetSessionData (DataSet ds, DebuggerSession session);

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
			: this (GetNextEventIndex (), name, group)
		{ }

		protected Event (int index, string name, ThreadGroup group)
		{
			this.index = index;
			this.name = name;
			this.group = group;

			if (group == null)
				throw new NullReferenceException ();
		}
	}
}
