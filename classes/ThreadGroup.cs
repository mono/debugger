using GLib;
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	// <summary>
	//   This is used to share information about breakpoints and signal handlers
	//   between different invocations of the same target.
	// </summary>
	public class ThreadGroup
	{
		string name;
		Hashtable threads;

		public ThreadGroup (string name)
			: this ()
		{
			this.name = name;
		}

		public ThreadGroup (IProcess process)
			: this ()
		{
			AddThread (process);
		}

		public ThreadGroup ()
		{
			this.threads = Hashtable.Synchronized (new Hashtable ());
		}

		public void AddThread (IProcess process)
		{
			if (!threads.Contains (process))
				threads.Add (process, true);
		}

		public void RemoveThread (IProcess process)
		{
			threads.Remove (process);
		}

		public IProcess[] Threads {
			get {
				lock (this) {
					IProcess[] retval = new IProcess [threads.Keys.Count];
					threads.Keys.CopyTo (retval, 0);
					return retval;
				}
			}
		}
	}
}
