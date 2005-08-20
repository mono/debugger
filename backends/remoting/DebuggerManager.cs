using System;
using System.Collections;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Remoting
{
	public class DebuggerManager : MarshalByRefObject
	{
		public readonly Guid Guid = Guid.NewGuid ();

		static int next_id = 0;
		private Hashtable clients = Hashtable.Synchronized (new Hashtable ());

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
			lock (clients.SyncRoot) {
				clients.Remove (client.ID);
				client.Shutdown ();
			}
		}

		public void Kill ()
		{
			lock (clients.SyncRoot) {
				foreach (DebuggerClient client in clients.Values) {
					client.DebuggerBackend.Dispose ();
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
			return new Process (sse);
		}
	}
}
