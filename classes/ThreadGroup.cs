using System;
using System.Collections;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;

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

		static ThreadGroup global = new ThreadGroup ("global");
		static ThreadGroup system = new ThreadGroup ("system");

		protected ThreadGroup (string name)
		{
			this.name = name;
			this.threads = Hashtable.Synchronized (new Hashtable ());
		}

		internal static ThreadGroup CreateThreadGroup (string name)
		{
			if ((name == "global") || (name == "system"))
				throw new InvalidOperationException ();

			return new ThreadGroup (name);
		}

		public void AddThread (int id)
		{
			if (IsSystem)
				throw new InvalidOperationException ();

			if (!threads.Contains (id))
				threads.Add (id, true);
		}

		public void RemoveThread (int id)
		{
			if (IsSystem)
				throw new InvalidOperationException ();

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

		public static ThreadGroup Global {
			get { return global; }
		}

		public static ThreadGroup System {
			get { return system; }
		}

		public bool IsGlobal {
			get { return this == global; }
		}

		public bool IsSystem {
			get { return this == global || this == system; }
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
				Process process = (Process) context.Context;

				string name = info.GetString ("name");
				return process.ThreadGroupByName (name);
			}
		}
	}
}
