using System;
using Mono.Debugger.Backend;
using System.Runtime.Serialization;
using System.Xml;

namespace Mono.Debugger
{
	[Serializable]
	public enum EventType
	{
		Breakpoint,
		CatchException,
		WatchRead,
		WatchWrite
	}

	public abstract class Event : DebuggerMarshalByRefObject
	{
		// <summary>
		//   Whether this breakpoint can persist across multiple
		//   invocations of the target.
		// </summary>
		public abstract bool IsPersistent {
			get;
		}

		public bool IsUserModule {
			get; set;
		}

		// <summary>
		//   The type of this event.
		// </summary>
		public EventType Type {
			get { return type; }
		}

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
			if (!IsEnabled)
				return false;

			if (group.IsSystem)
				return true;

			foreach (int thread in group.Threads) {
				if (thread == id)
					return true;
			}

			return false;
		}

		public bool IsEnabled {
			get { return enabled; }
			set { enabled = value; }
		}

		public abstract bool IsActivated {
			get;
		}

		public abstract void Activate (Thread target);

		public abstract void Deactivate (Thread target);

		public abstract void Remove (Thread target);

		internal abstract void OnTargetExited ();

		//
		// Session handling.
		//

		internal void GetSessionData (XmlElement root)
		{
			if (!IsPersistent)
				return;

			XmlElement element = root.OwnerDocument.CreateElement ("Breakpoint");
			root.AppendChild (element);

			element.SetAttribute ("index", Index.ToString ());
			element.SetAttribute ("type", Type.ToString ());
			element.SetAttribute ("name", Name);
			element.SetAttribute ("threadgroup", ThreadGroup.Name);
			element.SetAttribute ("enabled", IsEnabled ? "true" : "false");

			GetSessionData (root, element);
		}

		protected abstract void GetSessionData (XmlElement root, XmlElement element);

		//
		// Everything below is private.
		//

		private readonly int index;
		private readonly string name;
		private readonly EventType type;
		bool enabled = true;
		ThreadGroup group;
		static int next_event_index = 0;

		internal static int GetNextEventIndex ()
		{
			return ++next_event_index;
		}

		protected Event (EventType type, string name, ThreadGroup group)
			: this (type, GetNextEventIndex (), name, group)
		{ }

		protected Event (EventType type, int index, string name, ThreadGroup group)
		{
			this.type = type;
			this.index = index;
			this.name = name;
			this.group = group;

			if (group == null)
				throw new NullReferenceException ();
		}
	}
}
