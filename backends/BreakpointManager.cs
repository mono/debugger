using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	internal class BreakpointManager : IDisposable
	{
		IntPtr _manager;
		Hashtable breakpoints;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_new ();

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_clone (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_free (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup (IntPtr manager, long address);

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
			breakpoints = new Hashtable ();
			_manager = mono_debugger_breakpoint_manager_new ();
		}

		public BreakpointManager (BreakpointManager old)
		{
			Lock ();

			breakpoints = new Hashtable ();
			_manager = mono_debugger_breakpoint_manager_clone (old.Manager);

			foreach (int index in old.breakpoints.Keys) {
				Breakpoint breakpoint = (Breakpoint) old.breakpoints [index];
				breakpoints.Add (index, breakpoint);
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

		public Breakpoint LookupBreakpoint (TargetAddress address,
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
				return (Breakpoint) breakpoints [index];
			} finally {
				Unlock ();
			}
		}

		public Breakpoint LookupBreakpoint (int breakpoint)
		{
			Lock ();
			try {
				return (Breakpoint) breakpoints [breakpoint];
			} finally {
				Unlock ();
			}
		}

		public int InsertBreakpoint (Inferior inferior, Breakpoint breakpoint,
					     TargetAddress address)
		{
			Lock ();
			try {
				int index;
				bool is_enabled;
				Breakpoint old = LookupBreakpoint (address, out index, out is_enabled);
				if (old != null)
					throw new TargetException (
						TargetError.AlreadyHaveBreakpoint,
						"Already have breakpoint {0} at address {1}.",
						old.Index, address);

				int dr_index = -1;
				switch (breakpoint.Type) {
				case BreakpointType.Breakpoint:
					index = inferior.InsertBreakpoint (address);
					break;

				case BreakpointType.WatchRead:
					index = inferior.InsertHardwareWatchPoint (
						address, Inferior.HardwareBreakpointType.READ,
						out dr_index);
					break;

				case BreakpointType.WatchWrite:
					index = inferior.InsertHardwareWatchPoint (
						address, Inferior.HardwareBreakpointType.WRITE,
						out dr_index);
					break;

				default:
					throw new InternalError ();
				}

				breakpoints.Add (index, breakpoint);
				return index;
			} finally {
				Unlock ();
			}
		}

		public void RemoveBreakpoint (Inferior inferior, int index)
		{
			Lock ();
			try {
				inferior.RemoveBreakpoint (index);
				breakpoints.Remove (index);
			} finally {
				Unlock ();
			}
		}

		public void InitializeAfterFork (Inferior inferior)
		{
			Lock ();
			try {
				int[] indices = new int [breakpoints.Count];
				breakpoints.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					int idx = indices [i];
					Breakpoint bpt = (Breakpoint) breakpoints [idx];

					if (bpt.ThreadGroup.IsGlobal) {
						Breakpoint new_bpt = bpt.Clone (idx);
						inferior.Process.Session.AddEvent (new_bpt);
					} else if (!bpt.ThreadGroup.IsSystem) {
						RemoveBreakpoint (inferior, idx);
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
				int[] indices = new int [breakpoints.Count];
				breakpoints.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					int idx = indices [i];
					Breakpoint bpt = (Breakpoint) breakpoints [idx];

					RemoveBreakpoint (inferior, idx);
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
	}
}
