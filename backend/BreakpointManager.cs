using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backend
{
	internal class BreakpointManager : IDisposable
	{
		IntPtr _manager;
		Hashtable index_hash;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_new ();

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_clone (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_free (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup (IntPtr manager, long address);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup_by_id (IntPtr manager, int id);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_lock ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_unlock ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_breakpoint_info_get_id (IntPtr info);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_breakpoint_info_get_is_enabled (IntPtr info);

		public BreakpointManager ()
		{
			index_hash = new Hashtable ();
			_manager = mono_debugger_breakpoint_manager_new ();
		}

		public BreakpointManager (BreakpointManager old)
		{
			Lock ();

			index_hash = new Hashtable ();
			_manager = mono_debugger_breakpoint_manager_clone (old.Manager);

			foreach (int index in old.index_hash.Keys) {
				BreakpointEntry entry = (BreakpointEntry) old.index_hash [index];
				index_hash.Add (index, entry);
			}

			Unlock ();
		}

		protected void Lock ()
		{
			mono_debugger_breakpoint_manager_lock ();
		}

		protected void Unlock ()
		{
			mono_debugger_breakpoint_manager_unlock ();
		}

		internal IntPtr Manager {
			get { return _manager; }
		}

		public BreakpointHandle LookupBreakpoint (TargetAddress address,
							  out int index, out bool is_enabled)
		{
			Lock ();
			try {
				IntPtr info = mono_debugger_breakpoint_manager_lookup (
					_manager, address.Address);
				if (info == IntPtr.Zero) {
					index = 0;
					is_enabled = false;
					return null;
				}

				index = mono_debugger_breakpoint_info_get_id (info);
				is_enabled = mono_debugger_breakpoint_info_get_is_enabled (info);
				if (!index_hash.Contains (index))
					return null;
				return ((BreakpointEntry) index_hash [index]).Handle;
			} finally {
				Unlock ();
			}
		}

		public BreakpointHandle LookupBreakpoint (int index)
		{
			Lock ();
			try {
				if (!index_hash.Contains (index))
					return null;
				return ((BreakpointEntry) index_hash [index]).Handle;
			} finally {
				Unlock ();
			}
		}

		public bool IsBreakpointEnabled (int breakpoint)
		{
			Lock ();
			try {
				IntPtr info = mono_debugger_breakpoint_manager_lookup_by_id (
					_manager, breakpoint);
				if (info == IntPtr.Zero)
					return false;

				return mono_debugger_breakpoint_info_get_is_enabled (info);
			} finally {
				Unlock ();
			}
		}

		public int InsertBreakpoint (Inferior inferior, BreakpointHandle handle,
					     TargetAddress address, int domain)
		{
			Lock ();
			try {
				int index;
				bool is_enabled;
				BreakpointHandle old = LookupBreakpoint (
					address, out index, out is_enabled);
				if (old != null)
					throw new TargetException (
						TargetError.AlreadyHaveBreakpoint,
						"Already have breakpoint {0} at address {1}.",
						old.Breakpoint.Index, address);

				int dr_index = -1;
				switch (handle.Breakpoint.Type) {
				case EventType.Breakpoint:
					index = inferior.InsertBreakpoint (address);
					break;

				case EventType.WatchRead:
					index = inferior.InsertHardwareWatchPoint (
						address, Inferior.HardwareBreakpointType.READ,
						out dr_index);
					break;

				case EventType.WatchWrite:
					index = inferior.InsertHardwareWatchPoint (
						address, Inferior.HardwareBreakpointType.WRITE,
						out dr_index);
					break;

				default:
					throw new InternalError ();
				}

				index_hash.Add (index, new BreakpointEntry (handle, domain));
				return index;
			} finally {
				Unlock ();
			}
		}

		public void RemoveBreakpoint (Inferior inferior, BreakpointHandle handle)
		{
			Lock ();
			try {
				int[] indices = new int [index_hash.Count];
				index_hash.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					BreakpointEntry entry = (BreakpointEntry) index_hash [indices [i]];
					if (entry.Handle != handle)
						continue;
					inferior.RemoveBreakpoint (indices [i]);
					index_hash.Remove (indices [i]);
				}
			} finally {
				Unlock ();
			}
		}

		public void InitializeAfterFork (Inferior inferior)
		{
			Lock ();
			try {
				int[] indices = new int [index_hash.Count];
				index_hash.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					int idx = indices [i];
					BreakpointEntry entry = (BreakpointEntry) index_hash [idx];
					SourceBreakpoint bpt = entry.Handle.Breakpoint as SourceBreakpoint;

					if (!entry.Handle.Breakpoint.ThreadGroup.IsGlobal) {
						try {
							inferior.RemoveBreakpoint (idx);
						} catch (Exception ex) {
							Report.Error ("Removing breakpoint {0} failed: {1}",
								      idx, ex);
						}
					}
				}
			} finally {
				Unlock ();
			}
		}

		public void RemoveAllBreakpoints (Inferior inferior)
		{
			Lock ();
			try {
				int[] indices = new int [index_hash.Count];
				index_hash.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					try {
						inferior.RemoveBreakpoint (indices [i]);
					} catch (Exception ex) {
						Report.Error ("Removing breakpoint {0} failed: {1}",
							      indices [i], ex);
					}
				}
			} finally {
				Unlock ();
			}
		}

		public void DomainUnload (Inferior inferior, int domain)
		{
			Lock ();
			try {
				int[] indices = new int [index_hash.Count];
				index_hash.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					BreakpointEntry entry = (BreakpointEntry) index_hash [indices [i]];
					if (entry.Domain != domain)
						continue;
					inferior.RemoveBreakpoint (indices [i]);
					index_hash.Remove (indices [i]);
				}
			} finally {
				Unlock ();
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("BreakpointManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;

				mono_debugger_breakpoint_manager_free (_manager);
				_manager = IntPtr.Zero;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BreakpointManager ()
		{
			Dispose (false);
		}

		protected struct BreakpointEntry
		{
			public readonly BreakpointHandle Handle;
			public readonly int Domain;

			public BreakpointEntry (BreakpointHandle handle, int domain)
			{
				this.Handle = handle;
				this.Domain = domain;
			}

			public override int GetHashCode ()
			{
				return Handle.GetHashCode ();
			}

			public override bool Equals (object obj)
			{
				BreakpointEntry entry = (BreakpointEntry) obj;

				return (entry.Handle == Handle) && (entry.Domain == Domain);
			}

			public override string ToString ()
			{
				return String.Format ("BreakpointEntry ({0}:{1})", Handle, Domain);
			}
		}
	}
}
