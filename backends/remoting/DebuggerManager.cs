using System;
using ST = System.Threading;
using System.Collections;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Remoting
{
	public class DebuggerManager : MarshalByRefObject
	{
		public readonly Guid Guid = Guid.NewGuid ();
		public readonly ReportWriter ReportWriter;

		static int next_id = 0;
		private Hashtable clients = Hashtable.Synchronized (new Hashtable ());
		private Hashtable threads = Hashtable.Synchronized (new Hashtable ());
		private ST.ManualResetEvent interrupt_event = new ST.ManualResetEvent (false);

		private Hashtable thread_groups;
		private ThreadGroup global_thread_group;
		private ThreadGroup main_thread_group;

		public DebuggerManager (DebuggerOptions options)
		{
			if (options.HasDebugFlags)
				ReportWriter = new ReportWriter (options.DebugOutput, options.DebugFlags);
			else
				ReportWriter = new ReportWriter ();

			thread_groups = Hashtable.Synchronized (new Hashtable ());
			global_thread_group = CreateThreadGroup ("global");
			main_thread_group = CreateThreadGroup ("main");

			DebuggerContext.CreateClientContext (this);
		}

		int next_process_id = 0;
		public int NextProcessID {
			get { return ++next_process_id; }
		}

		long next_sequence_id = 0;
		public long NextSequenceID {
			get { return ++next_sequence_id; }
		}

		public DebuggerClient Run (string host, string remote_mono)
		{
			int id = ++next_id;
			DebuggerClient client = new DebuggerClient (this, id, host, remote_mono);
			clients.Add (id, client);
			return client;
		}

		public void TargetExited (DebuggerClient client)
		{
			clients.Remove (client.ID);
		}

		public void Wait (Process process)
		{
			if (process == null)
				return;

			ST.WaitHandle[] handles = new ST.WaitHandle [2];
			handles [0] = interrupt_event;
			handles [1] = process.WaitHandle;

			ST.WaitHandle.WaitAny (handles);
		}

		public void Interrupt ()
		{
			interrupt_event.Set ();
		}

		public void ClearInterrupt ()
		{
			interrupt_event.Reset ();
		}

		public void Kill ()
		{
			lock (this) {
				foreach (DebuggerClient client in clients.Values) {
					client.DebuggerServer.Dispose ();
					client.Shutdown ();
				}

				clients.Clear ();
			}
		}

		public bool HasTarget {
			get { return clients.Count > 0; }
		}

		internal Process CreateProcess (SingleSteppingEngine sse)
		{
			lock (this) {
				Process thread = new Process (this, sse);
				threads.Add (thread.ID, thread);
				return thread;
			}
		}

		internal Process CreateProcess (ThreadBase thread, int pid)
		{
			lock (this) {
				Process process = new Process (this, thread, pid);
				threads.Add (process.ID, process);
				return process;
			}
		}

		internal void ProcessExited (int id)
		{
			threads.Remove (id);
		}

		internal Process GetProcess (int id)
		{
			return (Process) threads [id];
		}

		//
		// Thread Groups
		//

		public ThreadGroup CreateThreadGroup (string name)
		{
			lock (thread_groups) {
				ThreadGroup group = (ThreadGroup) thread_groups [name];
				if (group != null)
					return group;

				group = new ThreadGroup (name);
				thread_groups.Add (name, group);
				return group;
			}
		}

		public void DeleteThreadGroup (string name)
		{
			thread_groups.Remove (name);
		}

		public bool ThreadGroupExists (string name)
		{
			return thread_groups.Contains (name);
		}

		public ThreadGroup[] ThreadGroups {
			get {
				lock (thread_groups) {
					ThreadGroup[] retval = new ThreadGroup [thread_groups.Values.Count];
					thread_groups.Values.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public ThreadGroup ThreadGroupByName (string name)
		{
			return (ThreadGroup) thread_groups [name];
		}

		public ThreadGroup MainThreadGroup {
			get { return main_thread_group; }
		}

		public ThreadGroup GlobalThreadGroup {
			get { return global_thread_group; }
		}
	}
}
