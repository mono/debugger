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
		static extern IntPtr mono_debugger_breakpoint_manager_new (BreakpointManagerMutexHandler lock_func, BreakpointManagerMutexHandler unlock_func);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_free (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup (IntPtr manager, long address);

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_breakpoint_info_get_id (IntPtr info);

		protected delegate void BreakpointManagerMutexHandler ();
		protected DebuggerMutex lock_mutex;

		public BreakpointManager ()
		{
			lock_mutex = new DebuggerMutex ("bpm_mutex");
			breakpoints = new Hashtable ();
			_manager = mono_debugger_breakpoint_manager_new (
				new BreakpointManagerMutexHandler (lock_func),
				new BreakpointManagerMutexHandler (unlock_func));
		}

		void lock_func ()
		{
			lock_mutex.Lock ();
		}

		void unlock_func ()
		{
			lock_mutex.Unlock ();
		}

		internal IntPtr Manager {
			get { return _manager; }
		}

		public Breakpoint LookupBreakpoint (TargetAddress address, out int index)
		{
			lock (this) {
				IntPtr info = mono_debugger_breakpoint_manager_lookup (
					_manager, address.Address);
				if (info == IntPtr.Zero) {
					index = 0;
					return null;
				}

				index = mono_debugger_breakpoint_info_get_id (info);
				return (Breakpoint) breakpoints [index];
			}
		}

		public Breakpoint LookupBreakpoint (int breakpoint)
		{
			lock (this) {
				return (Breakpoint) breakpoints [breakpoint];
			}
		}

		public int InsertBreakpoint (Inferior inferior, Breakpoint breakpoint,
					     TargetAddress address)
		{
			lock (this) {
				int index;
				Breakpoint old = LookupBreakpoint (address, out index);
				if (old != null)
					throw new TargetException (
						TargetError.AlreadyHaveBreakpoint,
						"Already have breakpoint {0} at address {1}.",
						address, old.Index);

				index = inferior.InsertBreakpoint (address);
				breakpoints.Add (index, breakpoint);
				return index;
			}
		}

		public void RemoveBreakpoint (Inferior inferior, int index)
		{
			lock (this) {
				inferior.RemoveBreakpoint (index);
				breakpoints.Remove (index);
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
