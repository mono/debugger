using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	// <summary>
	//   This is used to share information about breakpoints and signal handlers
	//   between different invocations of the same target.
	// </summary>
	[Serializable]
	public class ThreadGroup
	{
		static Hashtable groups = Hashtable.Synchronized (new Hashtable ());
		static ThreadGroup global_group, main_group;

		static ThreadGroup ()
		{
			global_group = CreateThreadGroup ("global");
			main_group = CreateThreadGroup ("main");
		}

		public static ThreadGroup CreateThreadGroup (string name)
		{
			ThreadGroup group = (ThreadGroup) groups [name];
			if (group != null)
				return group;

			group = new ThreadGroup (name);
			groups.Add (name, group);
			return group;
		}

		public static void DeleteThreadGroup (string name)
		{
			groups.Remove (name);
		}

		public static bool ThreadGroupExists (string name)
		{
			return groups.Contains (name);
		}

		public static ThreadGroup[] ThreadGroups {
			get {
				lock (groups) {
					ThreadGroup[] retval = new ThreadGroup [groups.Values.Count];
					groups.Values.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public static ThreadGroup ThreadGroupByName (string name)
		{
			return (ThreadGroup) groups [name];
		}

		string name;
		Hashtable threads;

		protected ThreadGroup (string name)
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

		public bool IsSystem {
			get { return (this == main_group) || (this == global_group); }
		}

		public bool IsGlobal {
			get { return this == global_group; }
		}

		public static ThreadGroup Main {
			get { return main_group; }
		}

		public static ThreadGroup Global {
			get { return global_group; }
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), name);
		}
	}
}
