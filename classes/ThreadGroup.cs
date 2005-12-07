using System;
using System.Collections;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;

using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	// <summary>
	//   This is used to share information about breakpoints and signal handlers
	//   between different invocations of the same target.
	// </summary>
	public class ThreadGroup : MarshalByRefObject
	{
		string name;
		Hashtable threads;

		internal ThreadGroup (string name)
		{
			this.name = name;
			this.threads = Hashtable.Synchronized (new Hashtable ());
		}

		public void AddThread (int id)
		{
			if (!threads.Contains (id))
				threads.Add (id, true);
		}

		public void RemoveThread (int id)
		{
			threads.Remove (id);
		}

		public int[] Threads {
			get {
				lock (this) {
					int[] retval = new int [threads.Keys.Count];
					threads.Keys.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public string Name {
			get { return name; }
		}

		public bool IsGlobal {
			get { return name == "global"; }
		}

		public bool IsSystem {
			get { return name == "global" || name == "main"; }
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), name);
		}

		internal sealed class SessionSurrogate : ISerializationSurrogate
		{
			public void GetObjectData (object obj, SerializationInfo info,
						   StreamingContext context)
			{
				ThreadGroup group = (ThreadGroup) obj;
				info.AddValue ("name", group.Name);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				DebuggerClient client = (DebuggerClient) context.Context;

				string name = info.GetString ("name");
				return client.DebuggerManager.ThreadGroupByName (name);
			}
		}
	}
}
