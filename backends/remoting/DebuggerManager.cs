using System;
using System.Threading;
using System.Collections;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Remoting
{
	public class DebuggerManager : MarshalByRefObject
	{
		public readonly Guid Guid = Guid.NewGuid ();

		static int next_id = 0;
		private Hashtable clients = Hashtable.Synchronized (new Hashtable ());
		private Hashtable threads = Hashtable.Synchronized (new Hashtable ());
		private ManualResetEvent interrupt_event = new ManualResetEvent (false);

		public DebuggerManager ()
		{
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
			WaitHandle[] handles = new WaitHandle [2];
			handles [0] = interrupt_event;
			handles [1] = process.WaitHandle;

			WaitHandle.WaitAny (handles);
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
					client.Debugger.Dispose ();
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

		internal void ProcessExited (int id)
		{
			threads.Remove (id);
		}

		internal Process GetProcess (int id)
		{
			return (Process) threads [id];
		}
	}
}
