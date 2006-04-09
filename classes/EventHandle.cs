using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public enum EventType
	{
		CatchException
	}

	public abstract class EventHandle : MarshalByRefObject
	{
		private ThreadGroup group;
		private int index;
		private string name;

		protected EventHandle (ThreadGroup group, string name, int index)
		{
			this.group = group;
			this.name = name;
			this.index = index;
		}

		// <summary>
		//   The breakpoint's name.  This property has no meaning at all for the
		//   backend, it's just something which can be displayed to the user to
		//   help him indentify this breakpoint.
		// </summary>
		public string Name {
			get { return name; }
		}

		// <summary>
		//   An automatically generated unique index for this breakpoint.
		// </summary>
		public int Index {
			get { return index; }
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
			if ((group == null) || group.IsGlobal)
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

		public abstract bool CheckBreakpointHit (Thread target, TargetAddress address);

		protected virtual void GetSessionData (SerializationInfo info)
		{
			info.AddValue ("group", group);
			info.AddValue ("name", name);
		}

		protected virtual void SetSessionData (SerializationInfo info, Process process)
		{
			index = Breakpoint.GetNextBreakpointIndex ();
			group = (ThreadGroup) info.GetValue ("group", typeof (ThreadGroup));
			name = info.GetString ("name");
		}

		protected internal class SessionSurrogate : ISerializationSurrogate
		{
			public virtual void GetObjectData (object obj, SerializationInfo info,
							   StreamingContext context)
			{
				EventHandle handle = (EventHandle) obj;
				handle.GetSessionData (info);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				EventHandle handle = (EventHandle) obj;
				handle.SetSessionData (info, (Process) context.Context);
				return handle;
			}
		}
	}
}
